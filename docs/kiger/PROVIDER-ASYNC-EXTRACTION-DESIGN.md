# Provider asynchronous extraction and runtime parity design

Status: design baseline; implementation not yet present. Branch: `kiger-dev`.
Scope: Eurostat Comext and any future provider whose API returns an extraction/job identifier before data is ready.

## 1. Why this is a separate runtime capability

The current `ICrmProspectingProvider.SearchAsync` contract is a single-call search contract. A successful HTTP response from a provider that merely acknowledges an asynchronous extraction is not a successful data search. Treating the acknowledgement as a completed result would incorrectly advance coverage and lose the extraction lifecycle.

Do not add Comext by blocking inside `SearchAsync`, sleeping/polling in a worker, or returning an empty `IsComplete=true` result. The async lifecycle must be durable and resumable across process restarts.

## 2. Backward-compatible boundary

Keep the existing `ICrmProspectingProvider` unchanged for synchronous adapters. Introduce a separate optional capability interface rather than adding mandatory members to every existing provider:

- `ICrmProspectingAsyncExtractionProvider`: provider code plus submit, poll, and fetch operations.
- `CrmProspectingExtractionSubmission`: provider extraction ID, accepted timestamp, optional provider expiry, bounded poll-after hint, and opaque provider cursor metadata.
- `CrmProspectingExtractionStatus`: Pending, Running, Succeeded, Failed, Expired, Cancelled, Unknown.
- `CrmProspectingExtractionPollResult`: normalized status, bounded retry hint, safe failure classification, and optional result cursor.
- `CrmProspectingExtractionPage`: records, next cursor, page completeness, provider-reported total when trustworthy, and safe warnings.

The application layer owns orchestration and persistence. Provider implementations own only the provider-specific HTTP protocol and normalization. Provider methods must not write CRM leads or mutate extraction rows.

## 3. Durable extraction record

Add a tenant-scoped entity/table with at least:

| Field | Purpose |
|---|---|
| Id / TenantId | Tenant identity and primary key scope |
| RunId | Owning prospecting Run |
| ProviderCode | Stable provider code |
| RequestFingerprint | Canonical query-scope idempotency |
| ProviderExtractionId | Opaque provider job ID; encrypt if provider policy or sensitivity requires |
| Status | Internal lifecycle state |
| SubmittedAtUtc / LastPolledAtUtc | Lifecycle timestamps |
| NextPollAtUtc | Due scheduling |
| ExpiresAtUtc | Provider or internal expiry bound |
| PollAttemptCount | Bounded polling |
| ResultCursor | Opaque continuation cursor, protected if it can contain credentials |
| LeaseOwner / LeaseToken / FencingVersion / LeaseExpiresAtUtc | Multi-worker claim and stale-worker fencing |
| FailureCode / SafeFailureSummary | Sanitized diagnostics only |
| CreatedAtUtc / UpdatedAtUtc | Audit |

Required unique key: tenant + run + provider + request fingerprint. Add indexes for tenant/status/next-poll and tenant/provider/provider-extraction-id. Do not rely on a process-local dictionary or in-memory timer.

## 4. State machine

Allowed transitions:

- Submitted -> Pending or Running
- Pending -> Running, Succeeded, Failed, Expired, Cancelled
- Running -> Running, Succeeded, Failed, Expired, Cancelled
- Succeeded -> FetchingResults
- FetchingResults -> FetchingResults or Completed
- Failed / Expired / Cancelled / Completed are terminal

Unknown provider status is not success: retain a safe Unknown/NeedsReview state, cap retries, and do not advance coverage. Provider status regression must not move a terminal row back to a live state.

## 5. Worker and recovery behavior

1. Submit only after the normal Run lease and request-ledger Started row are durable.
2. Persist the provider extraction ID and next poll time before acknowledging the submission step.
3. A scheduler enqueues due extraction IDs; workers claim rows with tenant scope, lease token, and monotonically increasing fencing version.
4. Poll only when due. Respect provider Retry-After and a bounded exponential backoff with jitter; enforce a maximum poll count and absolute expiry.
5. Fetch result pages in bounded batches. Persist normalized records and page cursor transactionally before advancing the cursor.
6. On restart, recover due Pending/Running/FetchingResults rows from the database. Duplicate queue messages must be harmless through conditional state updates and fencing.
7. Run cancellation must cancel or abandon provider work according to provider semantics, while retaining an auditable terminal state.
8. Tenant context must be established before resolving repositories, configuration, credentials, or provider services.

## 6. Coverage and request-ledger invariants

- Submission acknowledgement is never a completed provider result.
- Coverage advances only after the extraction reaches Succeeded, every required result page is fetched, all normalized records/evidence are durably persisted, and no page/record/size/run limit was hit.
- A failed, expired, cancelled, unknown, partially fetched, or truncated extraction leaves coverage unchanged.
- The request ledger should represent submission, polling, and result retrieval distinctly (or link child attempts to one extraction ID); do not count every poll as a new logical search.
- Retryable polling failures do not erase the extraction ID or prior cursor.
- Duplicate submission prevention must use the tenant-scoped request fingerprint; if the provider does not support idempotency keys, record the ambiguity and do not blindly resubmit after an uncertain network timeout.

## 7. Security and operations

- Fixed provider host allowlist, DNS/IP validation, redirects disabled, bounded response sizes, request timeouts, and strict cancellation.
- Credentials come from tenant-encrypted SaaS configuration; never place secrets in extraction IDs, cursors, warnings, exception messages, URLs, or logs.
- Sanitize provider errors before persistence and log only stable failure codes/status classes.
- Provider extraction IDs and cursors are untrusted opaque values: length-limit, validate allowed encoding, and never use them as URLs or filesystem paths.
- Enforce per-tenant active extraction caps, provider-wide concurrency caps, poll budgets, and retention policy.
- Expose safe operational status (queued, polling, retrieving, complete, incomplete, failed, expired) without exposing provider credentials or raw payloads.

## 8. Comext-specific constraints

- Use the Comext-specific base URL, not the general Eurostat dissemination API.
- Restrict reporter, partner, product codes, flow, indicators, and months before estimating cell count. Validate every code against the current official codelist/constraints.
- Keep requests below the synchronous threshold where practical; only use asynchronous extraction for bounded requests that legitimately cross the synchronous range.
- Never issue parallel broad extractions. Limit concurrency to one active Comext extraction per tenant initially and apply a provider-wide throttle.
- Trade observations remain aggregate trade signals. Do not create named importers or contact records from aggregate statistics.

## 9. Required acceptance tests before enabling a provider

- State transition matrix, including unknown status and terminal-state regression.
- Tenant isolation for reads, writes, queue messages, and credentials.
- Duplicate submission and uncertain-timeout behavior.
- Lease expiry, stale worker fencing, duplicate queue delivery, and process restart recovery.
- Retry-After, exponential backoff, max polls, expiry, cancellation, and provider 429/503 behavior.
- Multi-page fetch with cursor persistence, crash between page persistence and cursor update, and duplicate page replay.
- Coverage remains unchanged for every incomplete/failed/truncated path and advances only after full durable persistence.
- Secret redaction for request URLs, provider error bodies, extraction IDs/cursors, logs, APM, and proxy telemetry.
- PostgreSQL unique/index/migration tests and concurrent two-worker claim tests.
- Provider live smoke test using a narrow, low-cost, non-sensitive query.

## 10. Delivery sequence

1. Add entity, repository, migration, and state-transition tests.
2. Add async provider capability interfaces and adapter-neutral application orchestration.
3. Add due-work scheduler and recovery path reusing the established tenant lease/fencing conventions.
4. Add result-page persistence and coverage commit integration.
5. Implement Comext adapter only after codelist validation and a bounded query estimator exist.
6. Keep Comext disabled by default until PostgreSQL, restart recovery, tenant isolation, and official API smoke tests pass.

This document is an architecture baseline, not evidence that the async extraction implementation or Comext adapter has been completed.
