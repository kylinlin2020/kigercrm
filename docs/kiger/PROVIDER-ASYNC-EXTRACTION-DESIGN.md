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


### 11.8 Mandatory idempotent-submission registration gate

The async provider contract now exposes `SupportsIdempotentSubmission`. Every adapter must explicitly return `true` only when it uses a provider-documented idempotency key on submission or can authoritatively recover the original remote job by the same stable client key. The capability registry rejects async registrations that do not make this declaration.

This is a fail-closed registration guard, not proof that an adapter's implementation is correct. Each future adapter still needs provider-specific tests demonstrating that:
- the exact persisted `SubmissionIdempotencyKey` is sent unchanged on every retry;
- retry after a simulated timeout following remote acceptance returns or recovers the same remote extraction identity;
- credentials and idempotency tokens never enter logs, warnings, or persisted failure summaries;
- providers without a documented idempotency/recovery mechanism remain disabled and require manual review after an uncertain submission outcome.

No async provider adapter is enabled by this contract change. The capability declaration must be backed by provider documentation and adapter tests before rollout.


### 11.9 Guarded terminal completion

A repository review found that the generic lease-fenced `TryTransitionAsync` could previously be asked to move `FetchingResults` directly to `Completed`, bypassing the page-completion predicate. The repository now rejects `Completed` through the generic transition API.

The only completion path is `TryCompleteResultsAsync`, which requires `isLastPage=true`, `isComplete=true`, and `failureCount=0` through `CanCompleteResults`, then performs a tenant-, lease-token-, fencing-version-, and database-clock-guarded update from `FetchingResults` to `Completed`. This closes the generic transition bypass; the future page worker must call it only after the final page's Candidate/Evidence/Score and cursor transaction has committed.

### 11.10 Validate provider submission acknowledgements before persistence

Added `CrmProspectingExtractionSubmissionValidator` as an adapter-neutral validation boundary for remote Submit acknowledgements. It rejects:
- a missing acknowledgement or provider identity that does not match the selected adapter;
- blank, overlong, or control-character-bearing opaque extraction IDs;
- expiry timestamps that are not later than the provider's accepted timestamp;
- negative or unbounded poll-after hints;
- malformed, non-object, or oversized safe metadata JSON.

The validator does not treat an extraction ID as a URL, does not make a network request, and does not prove that the remote job exists. The future submission coordinator must invoke it before `TryAcknowledgeSubmissionAsync`; invalid acknowledgements must be recorded as sanitized failures/NeedsReview and must not be persisted as a pollable remote job. Contract tests cover provider mismatch, identifier bounds/control characters, expiry, retry hints, and metadata shape/size.

This is a validation primitive plus unexecuted test source. The async coordinator is still not wired, so no runtime path currently consumes the validator and no build/test result is claimed.

### 11.11 Persistence-boundary acknowledgement validation

The acknowledgement validator is now enforced inside `CrmProspectingExtractionJobRepository.TryAcknowledgeSubmissionAsync`, not only left as an adapter convention. The repository loads the tenant-scoped immutable intent to obtain its expected provider code, constructs the normalized acknowledgement, and rejects it before the conditional lease/fencing update if provider identity, opaque ID, expiry, or poll interval is invalid. The atomic PostgreSQL update still requires the exact tenant/job/status/lease token/fencing version and an unexpired database-clock lease.

The safe-metadata validator limit is aligned to the entity column's 4,000-character storage limit. The current repository acknowledgement method does not persist provider metadata; the coordinator must not add metadata persistence without applying the same bound and safe-field policy. Tests now cover leading/trailing whitespace in IDs and the exact metadata boundary. Source-only; no .NET build, tests, or PostgreSQL execution has been run.

### 11.12 Remote identity required for live lifecycle states

The generic `TryTransitionAsync` operation now refuses the initial `Submitting -> Pending` transition; that transition must go through `TryAcknowledgeSubmissionAsync`, which validates and persists the remote extraction ID under the exact live lease/fencing identity. Its SQL update also requires a non-null provider extraction ID for every live remote state (`Pending`, `Running`, `Succeeded`, `FetchingResults`, `Completed`). This protects recovery transitions from `NeedsReview` too: a review item without a known remote ID cannot be resumed into a pollable state. The state policy exposes `RequiresProviderExtractionId` and has contract tests for each lifecycle state.

This guard prevents local state from claiming a remote job exists when no remote identity was durably recorded. It does not replace end-to-end async orchestration, nor has its test source been executed in this environment.

### 11.13 Preserve remote identity in NeedsReview

Migration `5.9.34` initially modeled `NeedsReview` only as an uncertain submission with no remote ID. That is insufficient for an acknowledged extraction whose remote status becomes unknown or inconsistent: operators must retain the provider extraction ID for authoritative inspection, and an explicit recovery may resume polling only when that identity exists. Migration `5.9.35` replaces the submission-acknowledgement check constraint so `NeedsReview` supports both valid shapes: (a) no remote ID and no submitted timestamp for uncertain submission, or (b) a non-empty remote ID paired with a submitted timestamp for an acknowledged remote job. `Submitting` still requires both fields to be null; all other lifecycle states still require both to be present.

This is a forward-only compatibility migration; it does not rewrite existing job states or infer missing provider IDs. PostgreSQL migration execution and both NeedsReview persistence cases still require integration tests.

### 11.14 Due-work recovery for acknowledged NeedsReview jobs

The repository transition policy allows an operator-controlled `NeedsReview -> Pending/Running` recovery only when a provider extraction ID is already persisted. The due-job claim query now also admits a `NeedsReview` row only when both `provider_extraction_id` and `submitted_at_utc` are present. This makes acknowledged remote jobs recoverable for inspection/re-polling while keeping uncertain submissions (no remote ID) out of automatic retry. The claim leaves the lifecycle status unchanged; the future consumer must explicitly distinguish a claimed review row from ordinary Pending/Running work and must not submit it again.

No extraction consumer is registered yet, so this is repository-level recovery eligibility only, not an operational poller. A PostgreSQL integration test must verify both shapes and competing worker claims.


### 11.15 Keep the pure transition policy aligned with the acknowledgement boundary

The pure `CrmProspectingExtractionStateMachine.CanTransition` policy now also rejects `Submitting -> Pending`. Previously the repository already blocked that transition in `TryTransitionAsync`, but the pure policy still advertised it as legal, allowing callers and contract tests to reason from a weaker rule than persistence actually enforced. The only permitted path is `TryAcknowledgeSubmissionAsync`, which validates the provider acknowledgement and persists the remote extraction ID under the active tenant/job/lease/fencing identity. This keeps the domain policy, repository guard, and database write boundary aligned. The change is source-only until the .NET test suite is executed.


### 11.16 Fail closed when an async-only provider is selected by the synchronous Run worker

The current Run worker dispatches only `ICrmProspectingProvider`. It builds the capability registry with both sync and async registrations, but previously an async-only provider selected in the immutable Run plan fell through the generic "provider not registered" branch. That branch counted a provider failure and continued, allowing the Run to reach its ordinary terminal completion without ever submitting the async extraction.

The worker now distinguishes this case: if the selected code exists in the async capability map but has no synchronous implementation, it throws a classified orchestration-boundary exception. The Run follows its fenced failure path instead of appearing to have completed a plan whose async provider was never executed. This is deliberately fail-closed and temporary; it does not implement Submit/Poll/Fetch. A source-wiring regression test guards the distinction. The async extraction worker and Run continuation still need to be implemented before async-only providers can be enabled in approved plans.


### 11.17 NeedsReview is a human gate, not an automatic retry queue

The durable claim query previously included `NeedsReview` rows whenever a remote extraction ID and submitted timestamp existed. That made a manual-review state eligible for the same automatic worker claim path as Pending/Running jobs. A worker could then poll or otherwise act on a provider job whose state had explicitly been escalated for human inspection. This conflicts with the rule that unknown remote states and uncertain outcomes must stop automatic progression.

`ClaimDueAsync` now claims only Submitting, Pending, Running, and FetchingResults. `NeedsReview` is excluded regardless of whether a remote ID is present. It remains tenant-scoped and durable for inspection; a future explicit human approval/requeue application operation must be added before such a job can resume. This intentionally favors a visible paused job over unsafe automatic retries. A source-level regression test asserts that the claim SQL contains no NeedsReview predicate. PostgreSQL execution is still required to validate the complete SQL behavior.


### 11.18 Succeeded is a durable result-fetch work item

A lifecycle review found that `Succeeded` was a valid state before `FetchingResults`, but it was absent from both the automatic claim set and the expiry set. If a poller persisted Succeeded and released its lease before fetching the first page, the job could remain stranded forever: no worker could claim it to begin result retrieval, and expiry recovery would not terminalize it.

The claim query and expiry recovery now both include Succeeded. A later worker claim can transition Succeeded to FetchingResults and start bounded page retrieval. If the provider-side extraction expires before retrieval is complete, expiry recovery marks Succeeded jobs Expired as well. NeedsReview remains excluded from claims. This is a source-level correction; PostgreSQL scheduling/expiry behavior still requires integration testing.


### 11.19 Fenced lease renewal contract

Long-running asynchronous provider calls and multi-page result retrieval can outlive the initial worker lease. The extraction repository now exposes `TryRenewLeaseAsync`. Renewal is tenant-scoped and conditional on the exact job ID, lease token, fencing version, and a lease that is still live according to PostgreSQL `clock_timestamp()`. It cannot resurrect an expired lease, and it rejects durations outside the same 15-minute maximum used by initial claims. Only executable lifecycle states are renewable; NeedsReview and terminal states are excluded.

The method is a persistence primitive, not yet a running heartbeat. The future Extraction Worker must renew before expiry while provider calls/pages are in progress, cancel work immediately when renewal fails, and continue to use fenced writes for every state/cursor update. No background worker is wired by this change.


### 11.20 Cursor persistence boundary validation

The repository cursor CAS now independently rejects a non-null cursor that is blank or equal to the expected cursor, and bounds each persisted page count to the page validator's 100-record maximum. This protects the durable cursor from accidental replay loops and oversized accounting even if a future caller bypasses the page validator. A null next cursor remains available for a final page; the worker must still validate the page's final/complete markers and zero failure count before calling the completion operation. Cursor CAS continues to require the same transaction as candidate/evidence persistence and the live tenant-scoped fenced lease.


### 11.21 Run continuation and candidate-page reconciliation are hard prerequisites

A code-path review of `CrmProspectingRunHostedService` confirms that the synchronous Run currently owns the only complete candidate reconciliation pipeline. It performs, in order: tenant-scoped Lead duplicate lookup; exact provider/external identity dedupe; cross-Run Candidate/email/website dedupe; bounded Candidate normalization; fenced short-transaction Candidate + Evidence + Score insertion; unique-key race recovery with provenance repair; optional GLEIF exact-identity enrichment; and only then finalizes provider request-ledger coverage. The async extraction worker must reuse this pipeline rather than create a second, weaker Candidate insertion path.

The existing Run lifecycle currently has no durable waiting/continuation state: `Queued`, `Running`, terminal success/failure, and cancellation. Therefore the Run must not be completed after submitting an async extraction or while any extraction job is Pending, Running, Succeeded-but-not-fetched, FetchingResults, or NeedsReview. Before enabling async providers in a Run plan, add an explicit non-terminal waiting state (or an equivalently durable continuation record), persist the immutable Run snapshot and outstanding extraction IDs, and resume through a fresh Run lease after every required extraction reaches an accepted terminal outcome.

For each fetched page, one PostgreSQL transaction must include: Candidate identity reconciliation/insertion; Evidence and Score persistence; cursor compare-and-swap; and durable result-record accounting. The page cursor must never advance if any candidate/provenance write fails. Duplicate delivery must be idempotent through the existing unique identity key and provenance repair behavior. Only after every required extraction has reached Completed may the Run finalize provider request-ledger attempts and incremental Coverage. Failed, partial, truncated, expired, cancelled, or NeedsReview extraction outcomes must not advance coverage. A CancelRequested Run must stop new submissions, request remote cancellation where supported, and preserve an auditable non-success terminal result if remote cancellation is unavailable or uncertain.

Implementation order:
1. Extract the existing sync Candidate/Evidence/Score reconciliation into a reusable application service without changing its semantics; keep the sync Run path using that service.
2. Add the Run waiting/continuation persistence and recovery contract, including cancellation and multi-instance fencing.
3. Add the async Extraction Worker with tenant scope, lease heartbeat, lost-lease cancellation, and Submit/Poll/Fetch/Cancel handling.
4. Reuse the reconciliation service for each page and commit its writes with cursor CAS in one UoW.
5. Add PostgreSQL integration tests for crash/replay, duplicate pages, cursor conflicts, tenant isolation, stale fencing, multi-page completion, cancellation, and coverage non-advancement before wiring async providers into production plans.

Until these prerequisites are implemented and tested, the existing fail-closed async-provider selection guard must remain. This is a deliberate correctness gate, not a feature-complete async implementation.


### 11.22 Shared Candidate mapping extracted as the first reconciliation seam

The synchronous Run worker now delegates Candidate field normalization and entity construction to `CrmProspectingCandidateFactory`. The factory centralizes bounded source/title/company/contact/email/website/summary mapping, normalized email and website-host identity, and the PendingReview-versus-Duplicate status assignment. The caller still supplies duplicate evidence (existing Lead match, same-Run match, and cross-Run match) because those decisions require tenant-scoped repository queries and must not be guessed by a pure mapper.

The synchronous worker uses the factory for both batch duplicate-query keys and per-record Candidate creation. This establishes a shared mapping seam that the future asynchronous page reconciler can call without copying the Candidate field policy. Candidate + Evidence + Score transaction ownership, unique-key race recovery, GLEIF enrichment, and Coverage finalization remain in the Run worker for now; this is an incremental extraction, not yet the full reusable persistence application service.

Added focused factory tests for field bounds, normalization, duplicate classification, and required identity validation, plus a source-wiring regression check. These tests are authored but have not been compiled or executed. Next extraction step is to move the fenced Candidate/Evidence/Score UoW and provenance repair into a reusable application service while retaining the current sync behavior.


### 11.23 Candidate/Evidence/Score transaction extracted behind a shared persistence service

The synchronous Run worker now delegates first-time Candidate persistence to `CrmProspectingCandidatePersistenceService`. The service owns the explicit new transactional UoW, validates the current Run lease through the supplied lease token and fencing version, inserts Candidate, creates and persists field-level Evidence, evaluates the conservative initial score, persists the idempotent Score snapshot, and commits once. Provider I/O and tenant-scoped duplicate lookup remain outside the transaction.

The synchronous worker retains its existing exact unique-constraint recovery: after a Candidate identity race it reads the tenant-scoped winner and invokes the existing provenance repair path. Existing-Candidate repair, race recovery, optional GLEIF enrichment, and Run Coverage finalization are not yet moved into the shared service. The async page reconciler must call this same persistence service for new Candidates, but must additionally coordinate page-level cursor CAS and result accounting in the same UoW; the current method intentionally commits one Candidate at a time and is not yet the page transaction API.

The service is registered scoped alongside the CRM repositories. A source-wiring regression check was added. Tests have not been compiled or executed; no PostgreSQL transaction behavior is claimed. Before enabling async result pages, add a batch/page persistence entry point that accepts the active UoW or transaction boundary and commits all page Candidates/Evidence/Scores together with cursor CAS.

## 11.23 Opaque cursor transport validation (2026-11-05)

- The extraction page validator now rejects control characters in any non-null opaque cursor (invalid_cursor) in addition to the existing 2,048-character bound.
- This prevents newline/control-character injection into persisted cursor state and downstream diagnostics while preserving provider-specific printable cursor formats.
- A regression test covers an embedded newline and confirms the invalid page cannot advance the cursor.
- This is a boundary-hardening change only. It does not implement the async worker or the atomic Candidate/Evidence/Score + cursor transaction, and no test has been executed in this environment.

## 11.25 Repeated provider identity reconciliation inside page transactions (2026-11-05)

- The page persistence service now checks the tenant/Run/provider/normalized-external-ID identity before inserting a Candidate. Existing identities are reused rather than causing a duplicate insert on repeated records or a replay from an earlier synchronous path.
- For an existing Candidate, the service adds only missing Evidence identities and creates the initial Score snapshot only when no score exists. It does not rewrite Candidate review, duplicate, conversion, or ownership state.
- Evidence reconciliation, score repair, new Candidate creation, and the page cursor/status CAS remain inside the same page transaction. The extraction lease is the page-level fence; a stale cursor/lease still aborts the whole transaction.
- This improves idempotent reconciliation for repeated identities but does not replace end-to-end concurrency tests or worker orchestration. Unique-identity races across independently scheduled writers still require PostgreSQL integration coverage before production enablement.

## 11.26 Cumulative failures across extraction pages (2026-11-05)

- Added `result_failure_count` to the durable extraction job with migration 5.9.35 and a non-negative database constraint.
- Every non-final page transaction increments the cumulative failure count alongside the cursor and persisted-record count. The final-page transaction may complete only when the cumulative count plus the current page failures is zero; the SQL condition enforces this even if application state is stale.
- The page validator now rejects failure counts larger than the number of records in that page. Thus a clean final page cannot erase failures reported by an earlier page, and the extraction cannot be marked Completed (or later qualify for coverage finalization) after a partial result set.
- The new schema change is authored but has not been applied to a PostgreSQL database. Worker-level coverage gating and real database tests remain pending.

## 11.27 Commit final partial data without falsely completing extraction (2026-11-05)

- `CanCommitPage` now distinguishes page durability from successful extraction completion. A structurally complete final page may be persisted even when its record-failure count is nonzero.
- Added `TryFailIncompleteResultsAsync`: in the same transaction as Candidate/Evidence/Score writes, it commits the final page counts, sets the extraction to `Failed` with a fixed safe failure code/summary, and releases the lease. Its SQL requires a live exact lease/fencing version, expected cursor match, and a positive cumulative failure count.
- The page service selects `Completed` only when cumulative failures are zero; otherwise it commits the final page as `Failed`. This preserves successfully extracted candidates while ensuring partial results cannot advance coverage.
- This terminal path is authored but not yet exercised against PostgreSQL; Run-level coverage finalization still needs to explicitly require every extraction job to be `Completed`.

## 11.28 Run terminal-state gate against asynchronous extraction jobs (2026-11-05)

- `CompleteFencedWithDatabaseClockAsync` now checks the tenant/run-scoped extraction-job table in the same conditional PostgreSQL UPDATE that performs the Run terminal transition.
- A Run cannot become terminal while any extraction is `Submitting`, `Pending`, `Running`, `Succeeded` (provider succeeded but result pages not fetched), `FetchingResults`, or `NeedsReview`. This prevents early success and keeps uncertain jobs parked for human review.
- Once every extraction is terminal, `Failed`, `Expired`, and `Cancelled` jobs contribute to the Run's aggregate failure count. The Run becomes `Succeeded` only when both synchronous failures and asynchronous terminal failures are zero; otherwise it becomes `PartiallySucceeded` when raw records exist, or `Failed` when none exist.
- Added a source-level regression test for active-job blocking and terminal failure aggregation. This gate protects the Run final UPDATE; it does not yet implement async provider submission/poll/fetch scheduling or prove that request-ledger/Coverage finalization waits for all jobs. Job creation must be serialized under the Run lease before full async wiring, and PostgreSQL concurrency tests remain required.

- The completion predicate is fail-closed for future/unknown extraction status strings: only the four recognized terminal states (`Completed`, `Failed`, `Expired`, `Cancelled`) are allowed past the gate. This avoids a newly introduced lifecycle state being mistaken for a finished job.

## 11.29 Async page candidate identity binding (2026-11-05)

- Before opening the page transaction, the persistence boundary now loads the tenant-scoped Extraction Job and verifies `FetchingResults`, exact lease token/fencing version, and the expected cursor.
- Every candidate page item must map to one distinct normalized external identity present in the fetched page. Candidate Run ID, candidate/provider envelope codes, and candidate External ID must agree with the Extraction Job and raw record.
- This closes a prior validation gap: the page identity sets existed but were not used, so an incorrectly paired candidate/envelope could have been persisted under the wrong provider identity. Page cursor/fencing CAS remains the final in-transaction authority; preflight checks do not replace it.
- Added source-level regression assertions. Runtime and PostgreSQL transaction tests remain outstanding.

- The page persistence method explicitly opens a new transactional UnitOfWork before reading the job and writing candidates/evidence/scores; cursor advancement or final status CAS and `CompleteAsync` remain inside that transaction. Any failed identity, lease, or cursor check disposes the uncompleted UoW and rolls the page back.

## 11.30 Async page record accounting (2026-11-05)

- Page persistence now requires `candidateItems.Count + page.FailureCount == page.Records.Count`. Each fetched source record must be accounted for exactly once as a persisted candidate item or an explicitly declared record failure.
- This closes a silent-loss path where an adapter/mapping layer could omit one or more records from `items` while reporting zero failures; the extraction could otherwise advance its cursor or complete successfully despite records never reaching Candidate/Evidence/Score persistence.
- The existing one-to-one identity mapping still rejects duplicate candidate mappings and candidates not present in the fetched page. Runtime and PostgreSQL transaction tests remain outstanding.

### 11.31 Run-plan preflight for unsupported asynchronous providers (2026-11-05)

The Run worker now inspects the complete immutable provider plan before creating its in-memory result buffer, starting provider request-ledger attempts, or invoking any provider adapter. If the plan contains an async-only provider, the Run fails closed before any synchronous provider in that same plan can perform network I/O. This prevents a mixed sync/async plan from executing earlier synchronous providers and then discarding their in-memory results when the worker reaches an unsupported async provider.

This is a safety improvement only. It does **not** add durable Run waiting/resume, async submit/poll/fetch processing, or extraction completion recovery. Async-only providers remain unavailable to Run orchestration until those end-to-end pieces are implemented. The source-level guard test has been added but has not been executed.

### 11.32 Async provider registration contract guard (2026-11-05)

The provider registry enforces a single normalized provider code across synchronous and asynchronous capabilities, rejects blank/overlong codes, and refuses to register an asynchronous adapter unless it explicitly declares provider-documented idempotent submission support. This is required because a durable Submitting intent may be reclaimed after a process crash or an ambiguous network timeout; without provider-side idempotency or authoritative recovery by the stable key, retrying could create duplicate remote extraction jobs.

A source-level regression assertion now protects these registry invariants. This remains a registration-time safety contract, not a substitute for the still-missing extraction worker or Run continuation.

## 11.33 Run-level durable continuation design gate (2026-11-05)

The current Run worker is intentionally synchronous-only. Before enabling asynchronous adapters, implement a durable provider-plan checkpoint; an extraction-job row alone is not sufficient to resume its owning Run safely.

### A. Persist the immutable provider execution plan

Create a tenant-scoped Run-provider execution record for every distinct provider code in the Run's snapshotted plan, preserving the original plan ordinal. The record must include:

- RunId, TenantId, normalized ProviderCode, PlanOrdinal, and the immutable request fingerprint.
- A state such as Queued, Submitting, AwaitingExtraction, FetchingResults, Succeeded, Failed, Skipped, or Cancelled.
- Started/completed timestamps, bounded result/failure counters, safe failure code/summary, and row version.
- A unique key on (TenantId, RunId, ProviderCode) and an index supporting ordered due work.
- No raw credentials, remote response bodies, or unbounded provider metadata.

Materialize the complete plan before the first provider side effect. A Run restart must read this persisted plan rather than reconstruct progress from a mutable service registration list or an in-memory loop index.

### B. Make synchronous and asynchronous providers use the same checkpoint boundary

For a synchronous provider, persist a durable in-progress state before network I/O. Persist its bounded normalized output or durable candidate/evidence writes and its terminal provider state before moving to the next plan ordinal. A crash after remote success but before durable result persistence remains an uncertain attempt and must follow provider-specific idempotency/reconciliation policy; it must not silently count as success.

For an asynchronous provider, atomically link the provider execution record to the durable extraction job and move the provider execution to AwaitingExtraction only after the submission acknowledgement and extraction identity are durably committed. The extraction worker owns polling and page retrieval; the Run worker must not poll, sleep, or hold a Run lease while waiting for a remote job.

### C. Define the Run waiting and resume contract

Add an explicit non-terminal Run state (proposed name: AwaitingExtractions) only together with all of the following:

1. A conditional transition from Running to AwaitingExtractions that verifies the exact live Run lease and confirms at least one linked active extraction.
2. Release of the Run lease in the same database transaction as that transition; no heartbeat may continue renewing a waiting Run.
3. A durable resume signal/outbox entry written in the same transaction when the last required extraction reaches a safe terminal result.
4. A startup and periodic recovery scan that finds AwaitingExtractions Runs whose linked extraction work is already complete and recreates missing queue messages. Queue delivery remains at-least-once; database state is authoritative.
5. A conditional AwaitingExtractions -> Queued transition, preventing duplicate resume messages from creating parallel Run executions.
6. Resume logic that skips provider executions already durably Succeeded, handles Failed/Skipped according to the Run policy, and never repeats completed synchronous providers merely because a later async extraction finished.

Do not introduce the Run state before its release, outbox, recovery, and requeue paths are implemented. A state without a wake-up path creates permanently stranded Runs.

### D. Atomic result-page boundary

For each fetched page, one PostgreSQL transaction must:

- Validate tenant, Run, provider execution, extraction job, exact lease token/fencing version, and expected cursor.
- Persist all accepted Candidate/Evidence/Score rows and explicit per-record failures.
- Persist the page accounting result and extraction cursor/terminal transition using compare-and-swap.
- Update provider execution counters/state only after every record has an accounted outcome.
- Roll back all page writes if any candidate/evidence/score write, accounting check, or cursor CAS fails.

A replay of the same page must either be a harmless exact replay or fail the cursor CAS; it must never double-count records or advance past unpersisted data.

### E. Finalization and coverage rules

The Run can enter a terminal state only when every provider execution is in a terminal state and every linked extraction is Completed or safely terminal Failed/Expired/Cancelled. NeedsReview and unknown future states block finalization pending explicit resolution. Coverage advances only for a provider execution whose full result set is durably persisted, complete, non-truncated, and free of page/record failures. A successful sibling provider must not cause coverage to advance for an incomplete provider.

### F. Crash/recovery acceptance matrix

Before enabling any async adapter, add PostgreSQL-backed tests for at least these interruption points:

| Crash point | Required recovery behavior |
|---|---|
| After plan materialization, before first provider call | Resume from first Queued provider execution |
| After provider Started intent, before network response | Reconcile by stable idempotency key; otherwise NeedsReview |
| After synchronous response, before durable candidate/result commit | Reconcile or safely retry; do not mark provider Succeeded |
| After synchronous provider commit, before next provider | Skip the already Succeeded provider on resume |
| After async remote acknowledgement, before local acknowledgement commit | Retry only with provider-supported idempotency; otherwise NeedsReview |
| After extraction acknowledgement and provider-link commit | Poll the same persisted extraction; never submit a second job |
| After page candidate writes, before cursor CAS | Entire transaction rolls back |
| After page/cursor transaction, before queue acknowledgement | Redelivery is harmless; next fetch uses persisted cursor |
| After final extraction completion, before Run resume enqueue | Recovery scan/outbox recreates the resume signal |
| During duplicate Run resume delivery | One conditional claim succeeds; other deliveries are no-ops |
| After cancellation while extraction is active | Cancel/abandon remote work by adapter policy; no coverage advance |
| Tenant A queue message references Tenant B Run | Tenant-scoped lookup returns no claim; no cross-tenant mutation |

The tests must use two independent PostgreSQL connections for competing claims and stale-fence scenarios. Source-text assertions alone are not acceptance evidence.

### G. Implementation order

1. Add the Run-provider execution entity, repository, indexes/migration, and immutable-plan materialization.
2. Refactor the current synchronous Run loop to checkpoint and resume one provider at a time without changing current sync-provider behavior.
3. Add PostgreSQL crash/replay and duplicate-delivery tests for synchronous checkpoints.
4. Add the AwaitingExtractions transition, transactional outbox/recovery, and idempotent resume.
5. Implement the generic async extraction worker and transactional page reconciliation.
6. Add cancellation, lease-expiry, tenant-isolation, coverage, and failure-state tests.
7. Only then enable Eurostat Comext or another async production adapter.

This section is a design gate, not a claim that Run continuation or the async worker has been implemented.


## 11.34 Provider result checkpoint is distinct from request ledger (2026-11-05)

A code-path review of `CrmProspectingRunHostedService` confirms that the existing Provider Request Ledger is not a Run execution checkpoint:

- The request attempt is persisted before provider network I/O, but it records request lifecycle/diagnostics and coverage metadata, not the complete provider result set.
- Provider results are accumulated in the worker's in-memory `raw` list. The serialized raw payload is bounded and written only after the provider loop has completed.
- Candidate/Evidence/Score persistence follows that collection phase. A process interruption between provider return and durable candidate persistence can therefore lose returned records.
- Startup recovery can requeue queued Runs and recover interrupted request attempts, but it cannot reconstruct a completed provider response from the request ledger.

Consequently, do not infer provider completion from a successful request-ledger attempt and do not use that ledger as the sole replay checkpoint. A durable checkpoint design must persist a bounded, tenant-scoped provider result batch (or persist normalized candidate/evidence rows immediately) before marking that provider execution complete. The checkpoint identity must include Run, provider, immutable request fingerprint, and execution-plan version; writes must be fenced by the active Run lease and idempotent under duplicate delivery.

Required recovery semantics:

1. Provider response received but checkpoint not committed: treat the execution as uncertain; retry only where provider idempotency/rate and freshness policy permit.
2. Checkpoint committed but candidate projection incomplete: resume projection from the committed checkpoint without calling the provider again.
3. Candidate/evidence/score projection committed: mark the provider step complete in the same transaction, or use an equivalent transactional outbox/step ledger protocol.
4. Never advance Coverage merely because the HTTP request succeeded; Coverage may advance only after the full result checkpoint has been projected and all required candidate/evidence writes have committed.
5. Enforce per-provider and per-Run payload quotas, page/batch bounds, retention/cleanup, and redaction. Do not persist unbounded raw response bodies or credentials.
6. Recovery must use the immutable Run snapshot and provider-plan version, not current UI settings or a newly computed plan.

This is a confirmed implementation gap, not a claim that a checkpoint store or replay worker already exists. The synchronous Run worker remains single-pass/in-memory for provider results; async provider execution must remain fail-closed until the checkpoint and continuation protocol is implemented and PostgreSQL-tested.


## 11.35 Durable provider-execution checkpoint schema foundation (2026-11-05)

Added the first persistence foundation on `kiger-dev`:

- Entity: `SysCrmProspectingRunProviderExecution`.
- PostgreSQL migration: `5.9.36`, table `crm_prospecting_run_provider_execution`.
- Unique identity: tenant + Run + plan version + normalized provider code.
- Immutable execution identity fields: plan version, plan ordinal, provider code, request fingerprint.
- Explicit step lifecycle: Queued, Started, Checkpointed, Projecting, Succeeded, Failed, NeedsReview, Skipped.
- Bounded-checkpoint metadata: JSON payload, SHA-256 hash, byte count, raw/projected/failure counters.
- Database constraints validate state names, lifecycle completion timestamp shape, non-negative counters, fingerprint/hash shape, and checkpoint metadata consistency.

This is schema foundation only. It does not yet create or materialize execution rows, implement a repository, enforce the application-level checkpoint byte quota, persist provider results, project candidates, or resume Runs. Do not change the Run worker to consume this table until repository transitions are transactional and fenced by the live Run lease, the migration is exercised against PostgreSQL, and synchronous crash/replay tests pass. Existing Request Ledger semantics remain unchanged.


## 11.36 Fenced provider-execution repository foundation (2026-11-05)

Implemented the first repository boundary for the 5.9.36 execution-checkpoint table:

- `ICrmProspectingRunProviderExecutionRepository` exposes tenant-scoped Run reads, conditional step start, checkpoint save, and explicit state transitions.
- `CrmProspectingRunProviderExecutionRepository` validates PostgreSQL, current tenant, Run/execution identity, exact parent Run lease token and fencing version, and DB-clock lease expiry for mutations. Checkpoint and transition writes acquire a parent-Run row lock (`FOR UPDATE`) inside the caller transaction so lease takeover cannot race a child checkpoint commit.
- `TrySaveCheckpointAsync` requires an active caller transaction, validates JSON, enforces a 1 MiB UTF-8 payload ceiling, computes SHA-256 server-side, and permits only a single Started -> Checkpointed write when no prior checkpoint exists.
- Step transitions are allow-listed; terminal transitions require the caller transaction and set the terminal timestamp. Succeeded requires a projected count and zero failures.
- Repository is explicitly registered in CRM service collection.
- Source-level contract tests cover strict tenant entity identity, repository operations, lease fencing, transaction requirement, payload bound, and hash generation.

Important limitations: this repository does not yet materialize provider plan rows, provide an atomic batch insert, read/verify stored checkpoint hashes on recovery, persist Candidate/Evidence/Score projection with the step transition, or resume the Run worker. It also has not been compiled or executed against PostgreSQL. The synchronous worker remains unchanged and async providers remain fail-closed. The next step is safe immutable plan materialization and a PostgreSQL-backed repository test suite before any worker integration.


## 11.37 Immutable provider-plan materialization and recovery source (2026-11-05)

The five implementation tracks are being advanced together. This increment adds the durable plan layer and its first acceptance coverage:

1. **Snapshot -> execution rows:** `EnsurePlanMaterializedAsync` locks the exact live Run row and reads only `SnapshotProviderPlanJson`, `StrategyVersion`, and `PayloadHash`. It creates ordered tenant-scoped execution rows in one transaction.
2. **Idempotent initialization:** the parent Run row lock serializes concurrent materializers. An existing complete plan is returned unchanged; a partial or mismatching plan fails closed rather than being repaired or rewritten.
3. **Version and fingerprint:** `PlanVersion` is the immutable Run's `StrategyVersion`. Each provider fingerprint is SHA-256 over a versioned canonical tuple of Run payload hash, plan version, and normalized provider code. The unique database key remains tenant + Run + plan version + provider code.
4. **Recovery source:** the Run hosted worker now obtains provider codes and ordering from persisted execution rows on every delivery, rather than reparsing the snapshot as its execution list or reading mutable Strategy/Campaign configuration. The snapshot is used only by the repository to validate/materialize the immutable plan.
5. **PostgreSQL gate:** migration 5.9.36 now enforces the 1 MiB checkpoint quota and exact UTF-8 byte count at the database boundary. Added `backend/scripts/acceptance/Test-ProspectingProviderExecutionPostgres.sql` to test unique provider-plan identity, valid state constraints, and oversized checkpoint rejection against a disposable PostgreSQL database. SQLite mapping/tenant-scope integration coverage and source-contract guards were also added.

The repository checkpoint writes use a materialized parent-Run `FOR UPDATE` CTE referenced by the child update, so the lock is not merely declarative. A prior source review found and corrected an early CTE wiring defect in `TryStartAsync` and `TryTransitionAsync`.

**Still not complete / not claimed:** provider-result checkpoint replay, automatic reconciliation of ambiguous Started rows, Candidate/Evidence/Score projection atomic with terminal provider-step state, and the async extraction worker remain separate gates. The current Run worker reads its immutable plan from persisted rows, but does not yet drive every provider execution state transition. The PostgreSQL acceptance SQL and test suite have been authored but not run; Windows pull/build and real PostgreSQL concurrency acceptance remain pending. Async-only adapters stay fail-closed.


## 11.38 Interrupted provider execution is now fail-closed (2026-11-05)

Added a narrow recovery guard to the synchronous Run worker:

- A queued provider step is changed to Started only immediately before its request-ledger attempt is created and external provider invocation may begin. Unknown providers and active cooldown skips therefore remain Queued.
- If a later delivery finds a step still in Started, the worker treats the external outcome as ambiguous and conditionally changes it to NeedsReview under the exact live Run lease. It does not issue the provider request again.
- If a Checkpointed step is found, the worker also moves it to NeedsReview rather than invoking the provider again, because checkpoint hash verification, deserialization, and candidate projection replay are not yet wired.
- The reconciliation repository method owns a transaction, locks the parent Run row with a referenced FOR UPDATE CTE, and checks tenant, Run ID, lease token, fencing version, and DB-clock lease expiry before changing the child state.
- Contract tests now assert the recovery API, state allow-list, worker guard, and no-reinvocation behavior.

This is deliberately conservative and is not the finished checkpoint protocol. A normally completed provider invocation still does not yet atomically checkpoint its response, project Candidate/Evidence/Score, and mark the step Succeeded. The full response-replay and projection transaction remain outstanding; do not interpret NeedsReview reconciliation as successful recovery or as proof that every provider execution row reaches a terminal success state. Build/tests and PostgreSQL concurrency acceptance remain unrun.


## 11.39 Bounded provider response checkpoint and replay path (2026-11-05)

The synchronous Run worker now uses the provider-execution checkpoint table in its live response path:

1. After a synchronous provider returns, the worker creates a normalized checkpoint DTO containing records, completion flag, and provider failure count. It does not persist arbitrary raw HTTP bodies or provider warning text.
2. The checkpoint is bounded to 1 MiB UTF-8. Records are filtered against the existing per-record and per-Run retention limits before persistence; omitted records are counted as failures. An oversized serialized checkpoint is not silently discarded: the request ledger is finalized as failed and the execution step moves to NeedsReview.
3. TrySaveCheckpointAsync now owns its transaction. The update locks and validates the exact live tenant-scoped Run lease, stores the JSON, byte count and server-computed SHA-256, and changes Started -> Checkpointed atomically.
4. On redelivery, Checkpointed and Projecting rows are replayed from persisted JSON only after verifying the stored byte count, SHA-256 and record count. The worker does not call the provider again for those states. Replayed records are rechecked against per-record and per-Run limits.
5. Candidate/Evidence/Score projection uses the existing idempotent candidate identity and provenance-repair path. After projection, the worker advances Checkpointed -> Projecting -> Succeeded, or to Failed for incomplete/partial provider output. The state transition is fenced and transactional; a crash before that transition leaves the checkpoint available for another idempotent projection pass.

**Remaining atomicity boundary:** candidate/evidence/score writes are currently committed by their existing persistence units before the provider-step terminal transition. They are idempotent and replayable, but are not yet committed in the same database transaction as Projecting -> Succeeded. The next hardening step is to make the projection batch and terminal step transition share a single transaction (or a durable per-record outbox protocol). Provider request-ledger coverage also remains a separate post-projection operation. These code changes have not yet been compiled or run against PostgreSQL; the acceptance suite and Windows real-machine build remain pending.


## 11.40 Finalize provider-step outcomes for known failures and skips (2026-11-05)

The synchronous Run worker now also closes the provider execution ledger for ordinary non-success paths:

- A provider adapter that is not registered, or a provider skipped because its cooldown is active, transitions Queued -> Skipped in a transaction under the live Run fence.
- A provider invocation that is cancelled, rate-limited, throws, or returns a malformed envelope transitions Started -> Failed after the corresponding request-ledger attempt is finalized.
- If request-ledger finalization itself fails, the provider execution row is deliberately left Started; a later delivery will treat the external outcome as ambiguous and move it to NeedsReview rather than retrying blindly.
- These transitions are tenant-scoped and fenced by the same Run lease token, fencing version, and database-clock expiry. Source-contract tests guard the failure/skip helper paths.

This closes several previously stranded Queued/Started states. It does not change the remaining atomicity boundary: successful Candidate/Evidence/Score projection and provider-step Succeeded still use separate transactions, with idempotent checkpoint replay as the recovery mechanism.

## 11.41 Projection transaction boundary and next implementation gate (2026-11-05)

A follow-up review of the live synchronous worker confirms the exact remaining consistency boundary:

- The provider response is durably checkpointed before candidate projection.
- Candidate identity insertion and its evidence/score provenance are handled by the existing persistence service and repair path. Duplicate-key recovery re-reads the winning Candidate and repairs missing provenance without rewriting review state.
- The worker currently iterates the Run-wide raw-record list, rather than projecting one provider checkpoint as an isolated transaction batch. Provider projection failures are accumulated by provider code and applied when the execution rows are finalized.
- Each provider execution terminal transition is fenced and transactional, but it is committed separately from the Candidate/Evidence/Score persistence units. The checkpoint makes replay possible; it does not make these writes atomic.
- Request-ledger coverage is also finalized after projection and remains a separate commit boundary.

**Required next implementation (do not claim complete until all items are met):**

1. Refactor projection input into deterministic provider-scoped batches, preserving plan ordinal and checkpoint record order. Each batch must carry its execution-row ID, checkpoint hash, completion flag, original failure count, and normalized records.
2. Introduce a single persistence boundary that uses the same tenant database connection and ambient transaction for Candidate, Evidence, Score, provenance repair, and the fenced `Projecting -> Succeeded/Failed` transition. Verify the UnitOfWork and SqlSugar transaction actually enlist every repository; matching method scopes alone are not proof of a shared transaction.
3. Ensure candidate identity conflict handling is safe inside the batch transaction. A uniqueness conflict must not leave a partially committed evidence/score set; use database-native conflict handling or a savepoint/transaction strategy supported by the actual provider.
4. Persist provider-level projected/failure counters in the same commit as the terminal execution state. A replay of an already terminal batch must be a no-op after verifying its checkpoint identity.
5. Advance request-ledger coverage only after the provider batch transaction commits and only for a complete, zero-failure provider result. A crash between the batch commit and coverage update must be recoverable without repeating external provider I/O.
6. Add PostgreSQL integration tests with forced failures at Candidate insert, Evidence insert, Score insert, terminal transition, and post-commit coverage update. Assert all-or-nothing behavior for the batch, exact counters, stable replay, and no duplicate provider invocation.

Until this gate is implemented and exercised against PostgreSQL, the current checkpoint/replay path is a meaningful recovery improvement but remains a multi-transaction projection protocol. No build, test-suite, or PostgreSQL execution is claimed by this source review. The Windows pull/build checkpoint remains pending.

## 11.42 Atomic provider-batch persistence primitive added (2026-11-05)

Added `CrmProspectingCandidatePersistenceService.PersistProviderBatchAndTransitionAsync` as a shared transactional persistence primitive:

- Opens one transactional UnitOfWork and acquires the live Run lease fence before writes.
- Loads the provider execution row within the tenant-scoped Run and verifies the caller-supplied SHA-256 against the persisted checkpoint hash.
- Accepts only Checkpointed/Projecting rows; transitions Checkpointed -> Projecting inside the transaction.
- Validates every item belongs to the same Run/provider and that its external identity matches the normalized raw record.
- Reuses the existing idempotent Candidate/Evidence/Score persistence-and-repair routine for each item inside the batch UnitOfWork.
- Commits Projecting -> Succeeded or Projecting -> Failed in that same UnitOfWork. Any thrown persistence or transition error prevents UnitOfWork completion and rolls the batch back.
- A replay that finds the same checkpoint hash already terminal returns the persisted projected count without rewriting Candidate/Evidence/Score.
- Added a source-contract test for the checkpoint identity check, shared transaction boundary, and terminal transitions.

**Wiring status:** this primitive is now available through the registered persistence service, but the synchronous Run hosted worker has not yet been refactored to construct provider-scoped candidate batches and call it. Its existing path still performs candidate persistence and provider terminal transitions separately. Do not mark the synchronous atomicity gate complete until the worker uses this method, coverage advancement is reconciled after its commit, and PostgreSQL fault-injection tests prove the actual SqlSugar repositories enlist in the same transaction.

**Validation:** source-only. No .NET compilation, test execution, PostgreSQL transaction/fault-injection test, or Windows build is claimed.

## 11.43 Reject duplicate identities inside an atomic provider batch (2026-11-05)

The atomic batch primitive now validates normalized record identities as a batch-wide invariant before each Candidate/Evidence/Score write:

- The candidate Run and both provider codes must match the execution row.
- The candidate ExternalId must exactly match the normalized identity derived from its raw record.
- A batch-local ordinal identity set rejects repeated normalized external IDs before the second write can begin.
- The source-contract test asserts the duplicate guard runs before `PersistCandidateOrRepairAsync`.

This keeps `ProjectedRecordCount` meaningful as the number of distinct accepted provider records and avoids silently treating a malformed duplicate-containing batch as a clean provider projection. The guard is intentionally fail-closed: a caller must normalize/deduplicate and account for rejected rows in `failureCount` before retrying.

Worker integration, durable post-commit coverage reconciliation, PostgreSQL fault-injection tests, and runtime validation remain outstanding. The current synchronous worker still uses the legacy per-candidate persistence path.

## 11.44 Verify checkpoint integrity inside the atomic persistence boundary (2026-11-05)

The atomic provider-batch method no longer relies solely on its caller to verify the checkpoint:

- It checks the persisted JSON is present and its UTF-8 byte count matches the stored byte count.
- It recomputes SHA-256 from the persisted JSON and compares it with the stored execution hash.
- It deserializes the persisted checkpoint and checks its record count against `RawRecordCount`.
- Every candidate item must map to an identity actually present in the verified checkpoint; duplicate checkpoint rows remain allowed, while duplicate projected identities within one batch are rejected.

This closes a boundary gap for future worker callers: a correct database hash cannot be paired with an arbitrary, unrelated candidate payload. Added a source-contract regression test for byte count, digest, record count, and membership validation.

The synchronous Run worker is still not integrated with the atomic method. Coverage recovery, same-connection PostgreSQL transaction proof, fault-injection tests, and runtime compilation/testing remain outstanding.

## 11.45 Recover coverage after a committed provider batch (2026-11-06)

Closed the post-projection crash window for successful synchronous providers:

- Added a tenant-scoped `GetStartedAttemptAsync(runId, providerCode)` lookup. It returns the single unfinished request attempt and fails closed if more than one Started attempt exists for the same Run/provider pair.
- When a provider execution is already Succeeded on Run redelivery, the worker checks for its still-Started request attempt. If present, it verifies the persisted checkpoint byte count, SHA-256, completion flag, failure count, and record count before deriving the response digest.
- The worker then uses the existing PostgreSQL atomic `TryFinalizeSuccessWithCoverageAsync` statement to finalize the Started request and advance coverage under the current Run lease. It does not call the external provider again.
- Startup stale-request recovery now preserves a Started request when the same Run/provider has a Succeeded execution row with a durable checkpoint. This is essential because startup recovery runs before the queued Run is replayed; without this exclusion, the request would be marked interrupted before the worker could reconcile coverage.
- Added SQLite repository tests for single-attempt lookup and ambiguous Started attempts, plus a source-contract regression test for the recovery path.

This specifically repairs the crash window after the provider execution terminal commit but before request-ledger/coverage finalization. It does not yet wire the synchronous worker to `PersistProviderBatchAndTransitionAsync`; the worker's current Candidate/Evidence/Score projection still uses the legacy per-candidate path. Same-connection transaction proof, batch identity conflict strategy, PostgreSQL fault-injection tests, .NET compilation, and Windows real-machine validation remain pending.

## 11.46 Synchronous Worker atomic-batch cutover audit (2026-11-06)

### Existing recovery that is already implemented

The synchronous Run Worker already contains a deterministic post-projection recovery path; do not reimplement it as a new feature:

- GetStartedAttemptAsync(runId, providerCode) queries the tenant-scoped ledger for the single Started attempt for that Run/provider pair and throws if more than one exists. The request table's unique (tenant_id, run_id, provider_code, attempt_no) key and this explicit ambiguity check prevent guessing between multiple unfinished attempts.
- When the execution row is already Succeeded, the Worker verifies checkpoint UTF-8 byte count, SHA-256, deserialization, complete flag, zero provider failure count and raw record count. It derives the response digest from the verified checkpoint and calls TryFinalizeSuccessWithCoverageAsync for the Started request attempt. No external provider call is repeated.
- TryFinalizeSuccessWithCoverageAsync uses one PostgreSQL statement to fence and lock the live Run, transition the exact Started request to Succeeded, and upsert coverage. Request finalization and coverage advancement therefore commit atomically.
- RecoverStaleStartedAsync explicitly excludes a Started request if a matching Succeeded execution has a durable checkpoint, allowing the resumed Run to reconcile it rather than prematurely marking it interrupted.
- Source-contract coverage already guards the Worker recovery branch. This checkpoint also adds SQLite repository test sources for exact Run/provider/tenant lookup and fail-closed behavior when two Started attempts exist. These tests have not been executed in this environment.

This is a real recovery mechanism, not a missing feature. The association is derived by tenant + Run + provider and the invariant of exactly one Started attempt; there is not yet a direct request-attempt ID/attempt-number column on the execution row. That is a design choice to evaluate, not evidence that recovery is absent.

### Remaining synchronous Worker limitation

The current Worker still uses Run-wide candidate preparation and per-candidate PersistNewAsync; it does not yet call PersistProviderBatchAndTransitionAsync. Candidate/Evidence/Score persistence, provider execution terminal transition, and coverage finalization are separate durable stages. The checkpoint/replay and candidate identity repair paths make this recoverable, but they do not provide one atomic Candidate/Evidence/Score + provider-execution batch commit.

### Safe cutover requirements

1. Keep Run-wide deduplication semantics across providers. A provider-scoped batch must not reset shared identity, email, website, existing-lead, or cross-Run deduplication state.
2. Preserve existing-candidate provenance repair, unique-key winner recovery, and post-insert GLEIF enrichment. External GLEIF calls must remain outside a long-running candidate batch transaction.
3. Reconcile RawRecordCount - items.Count with the Worker's distinct rejection/failure classes. Duplicate or invalid raw rows cannot silently become a successful zero-failure provider batch.
4. After a provider batch commits, retain the existing Started-attempt recovery route unless request-ledger finalization is deliberately moved into the same proven transaction. Any changed flow must continue to use the exact attempt and the existing live Run fence.
5. Add PostgreSQL fault-injection tests for Candidate, Evidence, Score and terminal-transition failures; test replay after batch commit but before coverage finalization; assert no provider re-invocation, exact failure counters, and idempotent coverage. Prove all repositories share the same SqlSugar connection/transaction before claiming atomicity.
6. Add a multi-provider Run test proving shared deduplication, candidate repair and GLEIF evidence behavior remain unchanged after batch cutover.

### Gate and validation

Do not switch the Worker to the atomic batch primitive until the above invariants are preserved and PostgreSQL fault-injection tests pass. The existing post-projection coverage recovery is implemented, but the atomic provider-batch Worker cutover is not. No .NET build, automated test execution, PostgreSQL execution, or Windows pull/build acceptance is claimed for this source audit.

## 11.47 Submission-intent unique-race transaction safety (2026-11-06)

- `CreateSubmissionIntentAsync` first reads the tenant/run/provider/fingerprint identity and returns the existing intent when exactly one row exists. The database unique index remains the final concurrent-insert boundary.
- The repository no longer catches a generic `SqlSugarException` from `AddAsync` and immediately queries for a possible winner. On PostgreSQL, a uniqueness violation aborts the current transaction; querying through that same ambient transaction is not a valid recovery strategy and can mask the original error.
- Concurrent insert losers now propagate the persistence exception to the outer UnitOfWork boundary. Any future winner reconciliation must execute only after the failed transaction has rolled back/disposed, in a fresh UnitOfWork/connection, and must compare the complete tenant/run/provider/fingerprint tuple.
- Added a source-contract regression test asserting that the failed-insert catch-and-query pattern is absent. This test source has not been executed; no PostgreSQL concurrency result is claimed.

## 11.48 Run completion guard for unresolved extraction review (2026-11-06)

- `CompleteFencedWithDatabaseClockAsync` permits Run completion only when every extraction job for the same tenant and Run is in one of the explicit terminal states: Completed, Failed, Expired, or Cancelled.
- `NeedsReview` is intentionally not a terminal state for Run completion. A job whose remote submission/result outcome is ambiguous therefore continues to fence the parent Run until an authorized review action resolves it.
- The parent Run completion statement computes terminal extraction failures separately, so Failed/Expired/Cancelled jobs contribute to the Run failure count while Completed jobs do not.
- Added a source-contract regression test that protects this distinction. Test source was added but not executed; no PostgreSQL runtime claim is made.

## 11.49 Provider capability registry dependency-injection wiring (2026-11-06)

- The CRM module now registers one scoped `CrmProspectingProviderRegistry` factory built from the actual scoped `ICrmProspectingProvider` and `ICrmProspectingAsyncExtractionProvider` registrations.
- Registry construction therefore validates normalized provider-code uniqueness across both capability contracts when resolved, rather than leaving the registry as an unreferenced utility.
- Scoped lifetime is intentional: credential-aware provider adapters may depend on tenant-scoped SaaS configuration services and must not be captured by a singleton registry.
- Added a source-contract regression test for this DI wiring. No asynchronous adapter is enabled by this change, and it does not claim the Extraction Worker exists; submission, polling, page persistence and worker orchestration remain outstanding.
- Test source has not been executed; no .NET build/runtime validation is claimed.

## 11.50 Run worker consumes the scoped provider capability registry (2026-11-06)

- `CrmProspectingRunHostedService` now resolves the CRM-scoped `CrmProspectingProviderRegistry` from DI instead of constructing a second registry directly from service-provider enumerations.
- This makes the registered capability map the single validation point for provider-code uniqueness and async idempotency declarations within the Run scope; both sync and async adapters are still resolved with their registered scoped lifetime.
- Added a source-contract regression test that guards the worker's use of the shared scoped registry.
- This does not activate asynchronous Run execution: the worker still explicitly rejects async-only provider plan entries until a durable extraction consumer and Run continuation protocol exist. No async adapters are registered by this change.
- Test source has not been executed; no build or PostgreSQL runtime validation is claimed.


## 11.51 Persist validated provider acknowledgement metadata (2026-11-06)

- Closed a contract-to-storage gap in the durable submission acknowledgement path: `CrmProspectingExtractionSubmission.SafeMetadataJson` was validated by the submission validator but was not passed into the repository acknowledgement method or stored on the extraction job.
- `TryAcknowledgeSubmissionAsync` now accepts an optional safe-metadata JSON value, includes it in the same acknowledgement validation, and writes it to `safe_metadata_json` in the same fenced PostgreSQL update that records the provider extraction ID and transitions Submitting → Pending.
- The parameter is appended after the existing optional cancellation token to preserve existing positional call compatibility.
- Added source-contract coverage for the optional parameter, validator invocation, SQL column update and bound SQL parameter.
- This preserves provider-supplied non-secret operational metadata across worker restarts; it does not make arbitrary provider payloads safe. Adapters remain responsible for including only explicitly safe, bounded metadata.
- Test source has not been executed; no .NET build or PostgreSQL runtime validation is claimed.


## 11.52 Reject credential-bearing extraction metadata (2026-11-06)

- Tightened `CrmProspectingExtractionSubmissionValidator`: safe metadata must be a JSON object with a maximum nesting depth of 8 and must not contain property names indicating secrets, passwords, tokens, authorization headers, credentials, API keys, private keys or signing keys.
- The check recursively traverses nested objects and arrays, so wrapping a credential under a benign parent property does not bypass the guard.
- Property names are normalized by removing punctuation before matching, so variants such as `apiKey`, `api_key` and `api.key` are treated consistently.
- Added regression cases for camelCase API keys, snake_case access tokens, nested Authorization fields, nested client secrets and excessive JSON depth.
- The metadata contract remains intentionally allow-list-by-adapter: providers should return only bounded operational fields. This guard is a defensive rejection layer, not a general-purpose secret scrubber; arbitrary values must not be copied into metadata.
- Tests were added but have not been executed. No .NET build or runtime validation is claimed.


## 11.53 Tenant-scoped extraction expiry sweep worker (2026-11-06)

- Added `CrmProspectingExtractionExpiryHostedService` as the first hosted maintenance consumer for durable extraction jobs. It periodically uses `ITenantDataScopeRunner` to visit data-reachable tenants and invokes `ExpireDueAsync` only after entering a concrete tenant scope; the platform/null scope is explicitly skipped because extraction jobs are strict tenant data.
- The worker is single-concurrency and runs on a 30-second idle interval. It relies on the existing repository's PostgreSQL `clock_timestamp()` expiry predicate and does not scan provider payloads or log extraction IDs.
- Deliberately does not call `ClaimDueAsync`, submit/poll providers, or advance cursors. It is expiry housekeeping only—not the async extraction execution worker. The full Submit/Poll/Fetch/Project lifecycle and parent Run continuation remain outstanding.
- Added a source-contract test covering tenant-scope traversal, platform-scope exclusion, expiry invocation and the absence of accidental provider claims.
- Test/build/runtime validation has not been executed in this environment.


## 11.54 Async result-page text and identity hardening (2026-11-06)

- Page validation now rejects control characters in provider-controlled record text fields and source URLs before candidate/evidence projection. This prevents newline/control injection into persisted text and downstream logs.
- Duplicate record detection uses `CrmProspectingRecordIdentity.NormalizeExternalId`, so whitespace-normalized aliases cannot bypass the per-page duplicate guard and later collide during candidate projection.
- Provider warnings are bounded to 50 entries and 500 characters each, and reject control characters. The overall serialized page byte ceiling remains in force as a second resource bound.
- Added regression cases for normalized duplicate IDs, control-bearing company text, excessive warning count and newline-bearing warnings.
- Tests/build/runtime validation have not been executed in this environment.
## 11.55 Expiry recovery must preserve ambiguous submission outcomes (2026-11-06)

- Added a regression contract around ExpireDueAsync: only acknowledged provider jobs in Pending, Running, Succeeded or FetchingResults may be expired by the database-clock deadline.
- Submitting is deliberately excluded because a worker/network timeout may occur after remote acceptance but before local acknowledgement; expiry must not pretend that the remote outcome is known. Recovery must reuse the same provider-documented idempotency key or move to NeedsReview when the provider cannot authoritatively reconcile the submission.
- NeedsReview is also excluded from automatic expiry and remains a human-resolution state. Expiry clears stale lease ownership only for the acknowledged states it actually transitions.
- The added source-contract test is committed but has not been executed. No build, database or runtime validation is claimed.
### Regression test literal correction

- Corrected the control-character regression inputs to contain actual C# newline escape sequences (`\n`) at runtime, rather than the two-character backslash-plus-n text. The prior test source therefore did not exercise the control-character branch despite its name; the corrected source now does.
- The test source has been re-fetched and inspected after commit `2412e32cee5e0379a9adac9f7d4463fa9a26c63f`. Execution is still pending.
## 11.56 NeedsReview recovery must establish provider identity first (2026-11-06)

- Added a regression contract for the manual-recovery boundary: NeedsReview is not itself a provider execution state and is excluded from automatic ClaimDueAsync claims.
- The generic fenced transition path refuses to move a job into Pending or Running unless a provider extraction ID has already been persisted. This prevents an operator or future worker from converting an ambiguous submission into a pollable job without first resolving the remote identity.
- This does not yet implement the operator-facing reconciliation action that safely records a provider-confirmed extraction ID and requeues the job. Until that explicit action exists, unresolved NeedsReview jobs remain blocked and continue to prevent parent Run completion.
- Added source-contract coverage for the provider-ID precondition and manual-only claim behavior. Tests/build/runtime validation have not been executed.

## 11.57 Bounded exponential polling backoff contract (2026-11-06)

- Added a deterministic default polling schedule for asynchronous extraction jobs when a provider supplies no retry timestamp: one-based attempt counts start at five seconds, double per attempt, and cap at thirty minutes.
- Provider-supplied retry timestamps continue to pass through NormalizeRetryAtUtc, which clamps them to the five-second/thirty-minute scheduling window. The new default-delay helper is separate so the future worker can explicitly use the persisted poll-attempt count.
- Added regression cases for early backoff steps, cap behavior, non-positive attempt counts and UTC normalization.
- This is a scheduling policy primitive only; the Submit/Poll worker and its lease-heartbeat integration are still not implemented. Tests/build/runtime validation have not been executed.
