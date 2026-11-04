# Prospecting Provider Request Ledger and Coverage Design

> Status: request-ledger schema, repository lifecycle, Worker integration and stale Started recovery implemented on `kiger-dev`; PostgreSQL concurrency acceptance, coverage cursor, cache, deferred retry scheduling and circuit breaker remain pending.
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
| Request_Key | varchar(64) | SHA-256 of canonical safe request fingerprint |
| State | varchar(24) | Started, Succeeded, Failed, RateLimited, Cancelled |
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

Not yet implemented: coverage cursor, response cache, deferred retry scheduling/exponential backoff/circuit breaker, provider operational dashboard, PostgreSQL dual-connection concurrency tests, and Windows machine build/runtime acceptance. Tenant-scoped provider cooldown enforcement is implemented by querying persisted RateLimited request attempts before dispatch; it skips the provider while RetryAfterAt is active, but does not automatically defer/requeue the skipped provider. The SQLite repository tests and source-level contract tests are committed but have not been executed in this environment.

### Implemented prerequisite: canonical request fingerprint

The domain utility `CrmProspectingRequestFingerprint.Create(providerCode, request)` produces a SHA-256 fingerprint that excludes RunId, normalizes whitespace/provider-code casing, sorts and normalizes excluded domains, and canonicalizes JSON object property order. It rejects malformed provider-options JSON. Regression tests cover RunId independence, JSON property ordering, excluded-domain ordering, scope sensitivity, and malformed JSON. This is a deterministic fingerprint primitive; ProviderOptionsJson must remain free of credentials and secrets.

### Implemented: tenant-scoped request ledger schema (5.9.31)

The tenant-scoped append-only entity `SysCrmProspectingProviderRequest` and additive PostgreSQL migration `5.9.31/5.9.31.sql` define one row per provider invocation attempt. The schema contains tenant/run/provider identity, positive attempt number, 64-character request fingerprint, constrained lifecycle state, timestamps/duration, HTTP status, result count, bounded failure classification/summary, retry-after, response hash and cache-hit marker. The unique key is tenant + Run + provider + attempt number. Migration only creates the table where `crm_prospecting_run` exists; fresh databases use CodeFirst metadata. The entity is registered with CRM field-security metadata.

### Implemented: repository and Worker lifecycle

`ICrmProspectingProviderRequestRepository` is registered in CRM DI. The repository allocates the next 1-based attempt number per tenant + Run + provider and inserts Started before network I/O. The database unique key is the final concurrency arbiter: if two allocations race, the repository detects the winning attempt and retries allocation a bounded number of times; exhaustion fails closed. The Worker does not call the Provider unless the Started insert succeeded. The repository conditionally transitions Started to one terminal state and supports tenant-scoped attempt lookup.

The Worker checks for a tenant/provider cooldown from the latest future `RateLimited` ledger entry, then computes the canonical request fingerprint and creates Started before `SearchAsync`. It finalizes success, malformed result, provider exception, structured rate-limit and cancellation outcomes. A ledger write/finalization failure is fatal to the Run. Failure summaries are length-bounded, control-character sanitized, URL query/fragment values removed, and common Bearer/Basic/API-key/token/password assignments redacted. No raw HTTP headers or response bodies are stored.

### Implemented: interrupted-attempt recovery

Startup recovery runs after Run lease recovery and before queue reconstruction. It marks a Started attempt as Failed with stable code `worker_interrupted` only when it is older than the configured stale-attempt threshold (15 minutes) and its Run is missing, queued/terminal, or has an expired lease. It does not touch a request whose Run still has a live Running/CancelRequested lease. Recovery uses PostgreSQL database time and tenant-scoped SQL. The SQL still requires live PostgreSQL acceptance.

### Implemented: structured rate-limit outcomes

Contracts Finder, Find a Tender and EU TED raise a typed `CrmProspectingProviderRateLimitException` for their documented throttling responses (429/503; Contracts Finder also treats documented 403 as throttling). The exception carries HTTP status and parsed `Retry-After` date/delta, with a five-minute fallback if the provider supplies no header. The Worker persists `RateLimited`, HTTP status and retry-after timestamp instead of classifying the event as a generic failure.

The ledger-backed cooldown check now prevents a new invocation from being dispatched before Retry-After expires. Scheduler-aware deferred retries/requeue, exponential backoff/circuit behavior, and provider health diagnostics remain pending.


### Implemented slice: structured HTTP failures and Retry-After

TED, Find a Tender, Contracts Finder and USAspending now use the existing `CrmProspectingProviderRateLimitException` contract for documented throttle responses. HTTP 429/503 carry the provider's `Retry-After` date or delta; if absent, the adapter applies a bounded five-minute fallback. Contracts Finder additionally treats HTTP 403 as a five-minute cooldown signal, preserving its existing source-specific behavior. The Worker records `RateLimited`, HTTP status and `RetryAfterAt` in the request ledger. Other HTTP errors are raised as `HttpRequestException` with the status code, and the Worker records that status with a sanitized failure classification. Provider response bodies and arbitrary error text are not persisted.

Provider adapter tests cover TED 429, Find a Tender 503, Contracts Finder 403 and USAspending 429. These tests are source additions only until the .NET test suite is run. The durable cooldown check is implemented, but it skips the provider in the current Run rather than scheduling a deferred retry; a later Run may use the provider after the cooldown expires.


### Implemented: persisted cooldown enforcement from the request ledger

The repository queries the latest future `RetryAfterAt` among tenant-scoped `RateLimited` attempts for the provider, across Runs. Before creating a new Started attempt or making network I/O, the Worker checks this cooldown and skips the provider while it remains active. This reuses the append-only request ledger rather than introducing a second cooldown table. It does not sleep a worker thread or automatically requeue a partially completed Run: a later Run can use the provider after the cooldown expires. Deferred queue scheduling, exponential backoff and circuit-breaker state remain pending.


### Implemented: provider response digest

On a structurally valid provider result, the Worker now computes a SHA-256 digest over the returned record collection and stores only that digest in the request ledger's `Response_Hash` column. The helper returns no serialized payload and does not persist provider response bodies. This supports later audit/comparison of repeated responses without expanding sensitive raw-data retention. The digest is a byte-level serialization fingerprint, not a semantic canonicalization guarantee: equivalent objects serialized with different property/value representations may produce different hashes. The unit-test source covers stable equivalent inputs and changed content; execution remains pending with the rest of the .NET suite.


### Additional provider throttling coverage

The USAspending adapter also emits the structured 429/503 signal. GLEIF identity resolution now emits the same typed 429/503 signal with the provider Retry-After date/delta or a five-minute fallback. GLEIF lookups are auxiliary identity enrichment rather than a search-provider invocation, so their throttling is isolated as enrichment failure and is not yet written to the provider request ledger or shared cooldown gate.


### Shared diagnostic sanitization

A shared domain sanitizer now protects both request-ledger failure summaries and Run-level failure/warning summaries. It strips URL query/fragment suffixes, redacts Bearer/Basic values and common API-key/token/secret/password assignments, removes control characters, and enforces the caller's output bound. This prevents the broader Run failure path from bypassing the ledger's credential-redaction policy. Unit-test source covers URL, authorization, API key, password and length limits.
