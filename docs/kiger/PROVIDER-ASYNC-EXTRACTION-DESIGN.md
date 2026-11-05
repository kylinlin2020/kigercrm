# Provider asynchronous extraction and runtime parity design

Status: durable domain/persistence foundation implemented; end-to-end worker orchestration and provider adapter remain pending. Branch: `kiger-dev`.
Scope: Eurostat Comext and any future provider whose API returns an extraction/job identifier before data is ready.

## 1. Why this is a separate runtime capability

The current `ICrmProspectingProvider.SearchAsync` contract is a single-call search contract. A successful HTTP response from a provider that merely acknowledges an asynchronous extraction is not a successful data search. Treating the acknowledgement as a completed result would incorrectly advance coverage and lose the extraction lifecycle.

Do not add Comext by blocking inside `SearchAsync`, sleeping/polling in a worker, or returning an empty `IsComplete=true` result. The async lifecycle must be durable and resumable across process restarts.

## 2. Backward-compatible boundary

Keep the existing `ICrmProspectingProvider` unchanged for synchronous adapters. Introduce a separate optional capability interface rather than adding mandatory members to every existing provider:

- `ICrmProspectingAsyncExtractionProvider`: provider code plus submit, poll, and fetch operations.
- `CrmProspectingExtractionSubmission`: provider extraction ID, accepted timestamp, optional provider expiry, bounded poll-after hint, and opaque provider cursor metadata.
- `CrmProspectingExtractionStatus`: Submitting, Pending, Running, Succeeded, FetchingResults, Completed, Failed, Expired, Cancelled, NeedsReview. Remote provider statuses are normalized separately; unknown values map to `NeedsReview`.
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
| SubmissionIdempotencyKey | Stable local key persisted before remote submission |
| ProviderExtractionId | Nullable opaque provider job ID; encrypt if provider policy or sensitivity requires |
| Status | Internal lifecycle state, including pre-submit `Submitting` |
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

- Submitting -> Pending, NeedsReview, or Failed
- Pending -> Running, Succeeded, Failed, Expired, Cancelled, or NeedsReview
- Running -> Succeeded, Failed, Expired, Cancelled, or NeedsReview
- Succeeded -> FetchingResults, Failed, or Expired
- FetchingResults -> FetchingResults, Completed, Failed, or Expired
- NeedsReview may be explicitly resumed to Pending/Running or resolved to a terminal state
- Failed / Expired / Cancelled / Completed are terminal

Unknown provider status is not success: retain a safe Unknown/NeedsReview state, cap retries, and do not advance coverage. Provider status regression must not move a terminal row back to a live state.

## 5. Worker and recovery behavior

1. Persist a local `Submitting` intent and request-ledger Started row before provider network I/O; submission must use the persisted idempotency key.
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


## 11. Current implementation checkpoint (2026-11-05)

The following foundation is present on `kiger-dev`:

- Durable pre-submit intent is now represented explicitly by `Submitting`, nullable remote extraction ID/submitted timestamp, and a persisted tenant-scoped submission idempotency UUID. `CreateSubmissionIntentAsync` writes this row before network I/O; `ClaimDueAsync` can lease it; `TryAcknowledgeSubmissionAsync` records the remote ID only under the exact live lease/fencing identity. Migration `5.9.34` upgrades existing acknowledged rows without changing their state.
- The async provider Submit contract now receives the stable local idempotency UUID. An adapter must pass it through the provider's documented idempotency mechanism. If the provider does not support idempotency or lookup by client token, an uncertain timeout must be parked in `NeedsReview`; it must not be blindly resubmitted.

- Backward-compatible `ICrmProspectingAsyncExtractionProvider` contract and separate remote/local status enums.
- Local extraction state machine and status mapper; unknown numeric remote states map to `NeedsReview`.
- Tenant-scoped extraction job entity, repository, DI/entity registration, and PostgreSQL migration `5.9.33`.
- Due-job claim uses `FOR UPDATE SKIP LOCKED`, database-clock leases, UUID lease tokens, and monotonically increasing fencing versions.
- Status transitions and cursor advancement require the exact lease token/version and a still-live database-clock lease.
- Page validator bounds record count, cursor length, record field sizes, serialized page size, duplicate external IDs, source URLs, and final-page completeness.
- Unit/contract test sources cover state transitions, status mapping, page validation, and persistence contracts; they have not yet been executed in a .NET build/test environment.

The following end-to-end pieces are **not implemented**:

- No production async provider currently exercises the capability interface.
- The existing `CrmProspectingRunHostedService` still dispatches only `ICrmProspectingProvider.SearchAsync`; it does not submit, poll, or fetch async extraction jobs.
- No due-extraction scheduler/consumer is registered to claim and process extraction jobs.
- No extraction-page adapter persists page records into Candidate/Evidence/Score.
- No atomic transaction currently combines Candidate/Evidence/Score writes with extraction cursor advancement.
- No extraction completion path atomically links full durable result persistence to provider request-ledger finalization and Coverage.
- Comext adapter remains pending.

### 11.1 Required correction before worker integration: durable submission intent

The current job repository's `CreateAsync` accepts a non-empty remote `ProviderExtractionId`. Calling it only after the remote Submit response leaves a crash/timeout ambiguity: the provider may have accepted the request while the local database has no job row. A blind retry can create duplicate remote work.

Do not wire Submit directly to the current `CreateAsync` method. Before enabling async providers, evolve the local lifecycle to include a durable pre-submit intent:

1. Create a tenant/run/provider/fingerprint row in a local `Submitting` state before network I/O. The remote extraction ID and submitted timestamp remain nullable while submission is unresolved.
2. Give each intent a stable submission idempotency token derived from the persisted local job identity (not from secrets or mutable timestamps).
3. The provider Submit contract now accepts that token. Providers that support idempotency must send it using the provider's documented idempotency mechanism.
4. `TryAcknowledgeSubmissionAsync` persists the remote ID and accepted timestamps with a conditional update tied to the local job lease/token/version.
5. If a timeout occurs and the provider does not support idempotent submission or lookup by client token, transition to `NeedsReview` rather than automatically submitting again. The intent remains without a remote ID and is excluded from normal due polling until human reconciliation. Exactly-once remote submission cannot be guaranteed by local SQL alone.
6. Add unique-key race tests and crash-window tests for: before Submit, after provider acceptance but before local acknowledgement, and after acknowledgement persistence.

The migration must permit a null remote ID only for pre-acknowledgement states and enforce a non-empty remote ID for poll/fetch states. Status constraints, repository methods, and entity metadata must be changed together.

### 11.2 Worker integration boundary

Do not add a second global poller that has no tenant enumeration mechanism. The current Run worker already establishes tenant context and owns the Run lease. The first safe integration should be Run-scoped:

- The Run worker detects async-capable providers explicitly; it must not cast or infer async behavior from provider names.
- The async coordinator receives the already-established tenant scope, Run ID, Run lease token/version, request fingerprint, and immutable request snapshot.
- Remote polling must never hold a database transaction open.
- Each page persistence transaction must lock/validate the live Run lease and extraction-job lease, insert/reconcile Candidate/Evidence/Score, and compare-and-swap the cursor before commit.
- Page replay after a crash must be idempotent by Candidate identity and Evidence identity.
- Provider request-ledger attempts must distinguish one logical extraction submission from poll/page transport attempts; polling must not create a new coverage success.
- The Run cannot transition to Succeeded while any required async extraction remains Pending, Running, Succeeded-but-not-fetched, or FetchingResults.
- Cancellation and expiry must leave Coverage unchanged. The final Coverage update must occur only after all async extraction pages and existing synchronous provider results have been durably reconciled.

A later multi-tenant background extraction scheduler may be introduced only after a safe tenant enumeration/dispatch mechanism is identified and tested. Until then, do not run tenant-scoped repository calls from an unscoped global hosted service.

### 11.3 Acceptance gate

The async runtime remains disabled by default until all of the following are demonstrated:

- A registered async provider completes Submit → Poll → Fetch across multiple pages.
- Local submission intent prevents duplicate remote submissions or safely parks ambiguous submissions in NeedsReview.
- Run and extraction leases both fence stale workers, including after lease expiry but before another worker claims the row.
- Candidate/Evidence/Score persistence and cursor advancement are one transaction.
- A crash at every page boundary replays safely without lost records or duplicate evidence.
- Coverage and Run completion remain blocked for incomplete, failed, expired, cancelled, truncated, or NeedsReview jobs.
- Tenant-isolation, PostgreSQL concurrency, restart-recovery, migration, and provider live-smoke acceptance tests pass.
- A real .NET build/test and PostgreSQL integration run have been recorded; static code review alone is not acceptance.

Until these gates pass, the async capability and persistence foundation must be described as **partial implementation**, not completed runtime parity.

### 11.4 Source-level integration audit (2026-11-05)

A direct read of the current `CrmProspectingRunHostedService` confirms four concrete integration constraints:

1. Provider discovery currently resolves only `IEnumerable<ICrmProspectingProvider>` and indexes those synchronous implementations by `ProviderCode`. The async capability interface is not discovered or dispatched by the Run worker.
2. The synchronous provider loop builds one in-memory `raw` collection. Candidate/Evidence/Score reconciliation happens after the provider loop, so an async extraction cannot safely be treated as a normal in-memory provider result while it is still pending.
3. Coverage finalization iterates only the synchronous `pendingCoverage` collection after Candidate persistence. The Run then transitions to a terminal state through `CompleteFencedWithDatabaseClockAsync`. There is currently no durable “Run is waiting for async extraction” continuation boundary.
4. Candidate/Evidence/Score persistence is already implemented as a short explicit transaction per Candidate, guarded by the live Run lease. This is useful reusable logic, but it is not yet a page-level transaction with extraction cursor compare-and-swap.

Therefore the first worker implementation must not simply add async providers to the existing provider dictionary or return an empty provider result. It must introduce a durable provider execution outcome that distinguishes (a) synchronous completed results, (b) async extraction accepted/pending, and (c) failed/incomplete execution. The Run must not enter its terminal completion path while any required async extraction remains live or its results remain unfetched.

Implementation order for the next slice:

- Extract the existing Candidate/Evidence/Score reconciliation into a reusable service that accepts a bounded list of normalized envelopes and an existing Run lease identity; preserve current duplicate matching, provenance repair, conservative score creation, and short transaction behavior.
- Add an async page persistence method that reuses that service but executes Candidate/Evidence/Score writes and extraction cursor compare-and-swap inside one shared UnitOfWork transaction. The extraction repository must use the same database connection/transaction; verify this explicitly rather than assuming separate repository calls share it.
- Add a Run continuation state/queue contract so an accepted async extraction releases the current worker without completing the Run, and a later tenant-scoped worker resumes the same Run under a fresh Run lease. Persist enough state to reconstruct the immutable request and provider execution plan; never depend on the original in-memory request or queue message.
- Dispatch providers by explicit capability registration, reject duplicate provider codes across sync/async interfaces, and keep async providers opt-in. Do not use provider-name heuristics.
- Finalize the provider request ledger and Run coverage only after all pages are committed and every required provider outcome is complete. A page failure, cancellation, NeedsReview, expiry, limit hit, or cursor conflict leaves coverage unchanged.

No production async provider or async worker should be enabled until this continuation and transaction boundary exists and has PostgreSQL-backed crash/replay tests.

### 11.5 Close the direct-create bypass (2026-11-05)

A source audit found that the legacy repository `CreateAsync` API could still insert an already-acknowledged extraction row with a remote ID, bypassing the durable local `Submitting` intent. Although no production async provider currently uses this path, leaving it callable would preserve an unsafe integration route.

The method is now marked obsolete on the repository contract and its implementation fails closed with `NotSupportedException`. The only supported sequence is:

1. `CreateSubmissionIntentAsync` persists the tenant-scoped local intent.
2. Claim the intent and call Submit with its persisted `SubmissionIdempotencyKey`.
3. `TryAcknowledgeSubmissionAsync` records the remote ID only under the exact live lease and fencing version.
4. Ambiguous submissions without provider idempotency/lookup support are parked for review; never call the disabled direct-create path.

The persistence contract test now asserts the direct-create API is obsolete. This is a safety hardening slice, not evidence of a working async worker; no build or test execution is claimed.

### 11.6 Explicit provider capability registration (2026-11-05)

The synchronous Run worker previously grouped provider registrations by code and silently selected the first registration. That behavior is unsafe once asynchronous extraction providers coexist: duplicate codes across the synchronous and asynchronous contracts could route a Run to an arbitrary adapter.

Added `CrmProspectingProviderRegistry` as a single validation point:
- Normalizes provider codes using trim + invariant lowercase.
- Rejects null registrations, blank codes, codes longer than 80 characters, and duplicate codes.
- Detects collisions across both `ICrmProspectingProvider` and `ICrmProspectingAsyncExtractionProvider`.
- Keeps synchronous and asynchronous capability maps separate; it does not infer capability from provider names.
- The existing Run worker now builds this registry before dispatch and fails closed on ambiguous registrations.

Contract tests cover normalization, separate capability maps, cross-contract duplicates, blank codes, and length limits. This is registration hardening only: the Run worker still dispatches synchronous providers; asynchronous submit/poll/page execution and Run continuation are not yet wired.



### 11.7 Integration blockers confirmed during worker review

A fresh review of `CrmProspectingRunHostedService` confirms that it still resolves and dispatches only the synchronous `ICrmProspectingProvider` map. The async capability registry is constructed, but its async registrations are not yet consumed by a scheduler or extraction worker. Therefore an async-only provider selected in a Run is not executable today.

The existing synchronous worker also owns a substantial reconciliation path: exact provider/external-ID deduplication, cross-Run identity/email/domain matching, duplicate-lead matching, provenance repair, Candidate/Evidence/Score transactional persistence, optional GLEIF exact-identity enrichment, and the final request-ledger/Coverage commit. The async path must reuse this behavior rather than create a weaker second implementation.

The next implementation slice must be delivered in this order:

1. Extract a reusable candidate-page reconciliation application service from the synchronous worker without changing its existing behavior. Keep GLEIF enrichment separately bounded and outside the page transaction where appropriate.
2. Add an extraction-job worker and due-work recovery. Establish tenant context before resolving tenant configuration or provider services; claim jobs using the existing PostgreSQL lease/fencing API.
3. For every fetched page, validate the page, then persist Candidate/Evidence/Score and call `TryAdvanceCursorAsync` inside the same explicit UnitOfWork transaction. A failed/stale cursor compare must roll back all page writes.
4. Make replay safe: if the transaction committed, the cursor must have committed with it; if it rolled back, the page must be fetched again. Candidate/evidence/score identity constraints remain the final idempotency boundary.
5. Add a durable Run continuation design before wiring async submission into the Run path. The current Run lifecycle has no explicit waiting-for-extraction state, and the Run worker must not mark success or advance coverage while async work is pending. Preserve immutable Run snapshot inputs and resume under a fresh Run lease.
6. Finalize request-ledger attempts and advance Coverage only after all synchronous and asynchronous providers for the Run have completed fully and durably. Any failed, partial, truncated, expired, cancelled, or NeedsReview extraction must leave coverage unchanged.
7. Add crash/replay, tenant-isolation, stale-lease, duplicate-delivery, multi-page, cancellation, and coverage-invariant tests before enabling any async provider.

No async adapter should be enabled until these orchestration and PostgreSQL transaction tests exist. This checkpoint is a code-review finding, not a claim that async execution is complete.
