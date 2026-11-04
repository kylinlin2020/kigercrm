# Prospecting Provider Request Ledger and Coverage Design

> Status: implementation specification; this document does not claim that the ledger, cache, or coverage cursor is implemented.
> Target branch: `kiger-dev`. Keep PR #1 Draft and do not modify `main`.

## 1. Goals

Add durable observability and safe incremental collection without changing the provider contract's core rule: providers return source records and never create or update CRM Leads.

The design has three separate concerns:

1. **Request ledger** — one durable record for each provider invocation attempt.
2. **Coverage state** — a per-tenant, per-campaign, per-provider watermark describing what time range has been successfully scanned.
3. **Response cache** — optional short-lived reuse of an identical successful provider response, only where the provider's terms and freshness semantics allow it.

These concerns must not be collapsed into one table. A request attempt is an operational fact; coverage is a correctness claim; a cache entry is a performance optimization.

## 2. Request ledger model

Proposed tenant table: `Crm_Prospecting_Provider_Request`.

| Field | Type / constraint | Purpose |
|---|---|---|
| Basic_Id | bigint PK | Stable request-attempt identity |
| Tenant_Id | bigint, indexed | Tenant boundary |
| Run_Id | bigint, indexed | Parent execution |
| Provider_Code | varchar(80) | Stable provider key |
| Attempt_No | int | 1-based attempt within provider execution |
| Request_Key | char(64) | SHA-256 of canonical safe request fingerprint |
| State | smallint | Started, Succeeded, Failed, RateLimited, Cancelled |
| Started_At / Completed_At | timestamptz | Operational timing |
| Duration_Ms | bigint nullable | Measured elapsed duration |
| Http_Status | int nullable | Sanitized response status |
| Result_Count | int | Number of source records accepted from this attempt |
| Failure_Code | varchar(80) nullable | Stable, non-secret classification |
| Failure_Summary | varchar(1000) nullable | Sanitized bounded diagnostic |
| Retry_After_At | timestamptz nullable | Provider-requested cooldown |
| Response_Hash | char(64) nullable | Hash of retained response, never raw secrets |
| Cache_Hit | boolean | Whether this attempt reused an eligible cache entry |

Required indexes:
- unique (Tenant_Id, Run_Id, Provider_Code, Attempt_No)
- (Tenant_Id, Provider_Code, Started_At DESC)
- (Tenant_Id, State, Started_At DESC)
- (Tenant_Id, Request_Key, Started_At DESC)

The request fingerprint must be computed from a canonical serialization of provider code, normalized search objective, product scope, target markets, ICP fingerprint, normalized excluded-domain set, date window, and provider options. It must not contain API credentials, authorization headers, cookies, or personally identifying contact values.

## 3. State machine and write protocol

`Started → Succeeded | Failed | RateLimited | Cancelled`

- Insert Started before making the network request.
- Finalize exactly once using a conditional update where state is Started.
- A process crash may leave Started rows. A maintenance/recovery operation marks old Started rows as Failed with `worker_interrupted`; it must not infer success.
- A retry creates a new Attempt_No row. Never overwrite the earlier attempt.
- Cancellation is recorded as Cancelled only when cancellation is attributable to the Run/host token. A provider timeout is Failed, not Cancelled.
- Failure messages are length-limited, control-character sanitized, and must redact URLs' query strings and known credential patterns.
- Do not persist full HTTP request/response headers or arbitrary provider error bodies.

The provider invocation should be bracketed by ledger writes but provider exceptions must remain isolated per provider, preserving the current behavior where later providers continue after one provider fails.

## 4. Coverage cursor model

Proposed tenant table: `Crm_Prospecting_Provider_Coverage`.

Natural key: (Tenant_Id, Campaign_Id, Provider_Code, Coverage_Scope_Hash).

Fields:
- Last_Successful_Window_Start / Last_Successful_Window_End (timestamptz)
- Last_Successful_Run_Id
- Last_Successful_Request_Key
- Updated_At
- Version (optimistic concurrency counter)

Rules:
1. Only a fully successful provider window may advance coverage.
2. Partial provider results, page truncation, 429/403/503, timeout, malformed pagination, and cancellation do not advance the cursor.
3. Use an overlap window when constructing the next scan (default 48 hours) to tolerate delayed publication and provider indexing lag.
4. Deduplication remains the idempotency backstop; cursor advancement must never be used as the only duplicate-prevention mechanism.
5. Cursor update must occur in the same transaction as finalizing the provider request ledger success, and only if the expected previous Version still matches.
6. If a provider only exposes a rolling window or opaque cursor, store its provider-specific cursor separately from the normalized publication-time watermark. Do not pretend a rolling 30-day API is an exhaustive archive.
7. The provider's maximum page/result cap must be represented in the request outcome. A truncated scan is not complete coverage.

## 5. Cache policy

Cache only when the provider explicitly permits reuse and the response is safe to retain.

- Cache key: SHA-256 of the canonical request fingerprint.
- Cache values must be tenant-scoped unless the source data is demonstrably public and the implementation has an explicit cross-tenant sharing policy.
- Default TTL: 15 minutes; provider-specific configuration may shorten it, not extend it without documented justification.
- Never cache authentication failures, rate-limit responses, partial pages, malformed payloads, or transient server failures.
- Never treat a cache hit as a new coverage success unless the original complete response's coverage metadata is still valid for the same requested window.
- Keep cache eviction bounded by both TTL and a configurable maximum retained byte count.
- Cache is disposable. Deleting it must not affect ledger, candidate, evidence, or coverage correctness.

## 6. Rate limiting and circuit behavior

Persist provider cooldown per tenant and provider. Respect explicit Retry-After when supplied; parse both delta-seconds and HTTP-date forms, clamp to a safe configured maximum, and never retry before the cooldown expires.

Initial policy:
- 429 / documented rate-limit 403: open cooldown; no immediate retry.
- 503 with Retry-After: defer until the indicated time.
- repeated 5xx/timeouts: exponential backoff with jitter and a bounded attempt count.
- 4xx other than documented throttling: fail fast; do not retry.
- malformed JSON/schema or unsafe pagination URL: fail closed.
- circuit state is operational and must be visible in provider health diagnostics.

Do not sleep inside a worker for long cooldowns. Persist the deferred retry time and return control to the queue scheduler.

## 7. Security and privacy

- Every query and write is tenant-scoped.
- Provider credentials remain in the existing secret/configuration boundary and never enter request fingerprints or ledger diagnostics.
- Fixed provider host/path allowlists, HTTPS-only, redirects disabled, response-size limits and bounded timeouts remain mandatory.
- External provider text is untrusted. Sanitize before logging or displaying.
- Do not add bulk contact harvesting or automated outreach as a side effect.
- Candidate-to-Lead conversion remains a separate human-approved CRM action.

## 8. Migration and rollout sequence

1. Add entity mappings, repositories, tenant metadata and additive migration for the request ledger only.
2. Wire ledger Started/finalization around one low-risk provider (Fake) and add SQLite repository tests.
3. Add PostgreSQL tests for unique attempt identity, concurrent finalization, tenant isolation and crash-left Started recovery.
4. Wire remaining providers without changing their existing page/time/response caps.
5. Add coverage table and atomic cursor advancement only after ledger lifecycle is verified.
6. Add cache only after correctness and source-term review; cache can be disabled independently.
7. Add provider cooldown/circuit state and scheduler-aware retry.
8. Expose read-only operational diagnostics with existing CRM read permissions and tenant/data-scope guards.

Do not combine the entire rollout into one migration or enable cache/cursor advancement by default before PostgreSQL acceptance.

## 9. Required tests

- Request Started is persisted before provider network invocation.
- Every provider outcome finalizes one and only one attempt.
- Duplicate finalization is rejected/no-op and cannot rewrite a terminal outcome.
- A crashed Started request is marked interrupted, not successful.
- Concurrent attempts receive distinct Attempt_No values.
- Request fingerprint is stable under irrelevant JSON property order and excluded-domain order.
- Secrets, authorization headers, query credentials and raw error bodies never appear in the ledger.
- Partial/truncated results do not advance coverage.
- Successful complete windows advance coverage atomically and use optimistic concurrency.
- Overlap windows and provider rolling-window semantics are honored.
- Cache never serves across tenants by default and never converts a failed/partial response into coverage.
- Retry-After delta/date parsing, 429/403/503 cooldown, exponential backoff and no worker-thread sleeping.
- Tenant isolation and permission checks on diagnostics.
- PostgreSQL dual-connection races and Windows migration/build/runtime acceptance.

## 10. Current implementation boundary

Already present on `kiger-dev`: bounded provider result retention, per-provider exception isolation, source evidence hashes, cross-Run candidate deduplication, lease/fencing lifecycle, independent heartbeat, and human review before CRM Lead conversion.

Not yet implemented by this design document: durable provider request ledger, coverage cursor, response cache, persisted cooldown/circuit breaker, provider operational dashboard, and their database-backed tests.
