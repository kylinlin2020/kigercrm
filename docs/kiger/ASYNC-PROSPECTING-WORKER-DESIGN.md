# Durable Asynchronous Prospecting Worker — Implementation Contract

> Status: implementation design / gap analysis. This document does not claim that an extraction consumer is implemented.
> Scope: `work/a-async-extraction`, based on the current provider contracts, extraction repository, candidate persistence service, and Run worker.

## Existing building blocks verified in the branch

- `ICrmProspectingAsyncExtractionProvider` exposes idempotent submission, polling, one-page result retrieval, and remote cancellation.
- `CrmProspectingProviderRegistry` keeps synchronous and asynchronous provider codes unique and rejects async providers without documented idempotent submission.
- `SysCrmProspectingExtractionJob` persists tenant, Run, provider, request fingerprint, stable submission key, remote extraction ID, state, poll schedule, cursor, failure counters, lease token, and fencing version.
- `ICrmProspectingExtractionJobRepository` provides durable submission intent, acknowledgement, PostgreSQL `SKIP LOCKED` claims, live-lease renewal, state transitions, expiry, cursor CAS, and final-page completion.
- `CrmProspectingCandidatePersistenceService.PersistPageAndAdvanceAsync` commits candidate/evidence/score projection and cursor/finalization in one transaction. It rejects incomplete or non-advancing pages and validates that each projected record maps one-to-one to the fetched page.
- `CrmProspectingRunHostedService` currently rejects async-only provider plans before any synchronous provider I/O. This is intentional fail-closed behavior, not async execution support.

## Required worker lifecycle

1. **Tenant scope:** each claimed job must execute under its persisted tenant. Never trust a queue message for tenant identity; the repository claim result is authoritative.
2. **Lease discipline:** claim with a unique worker owner; heartbeat with the exact job ID, lease token, and fencing version. If renewal fails, cancel the operation and perform no further writes.
3. **Provider resolution:** resolve the exact normalized provider code from the scoped registry. A missing provider or mismatched remote reference must transition to a safe failure/review state, not fall back to another provider.
4. **Submission:** only `Submitting` without a remote ID may submit. Reuse the persisted `SubmissionIdempotencyKey` on every retry. Persist the acknowledgement only through `TryAcknowledgeSubmissionAsync` while the exact live lease is held. An ambiguous timeout is retryable only because registration requires provider-documented idempotency/recovery.
5. **Polling:** only `Pending`/`Running` with a remote ID may poll. Use `CrmProspectingExtractionPollPolicy.EvaluateForCurrentState`; unknown values, status regression, and invalid local state must fail closed to `NeedsReview`.
6. **Result retrieval:** transition `Succeeded` to `FetchingResults` before fetching. Fetch exactly one bounded page at a time. Validate page size, cursor syntax, last-page/completeness semantics, and warning/failure counts before projection.
7. **Atomic page commit:** build candidate/evidence/score page items using tenant-scoped duplicate lookup, then call `PersistPageAndAdvanceAsync`. The cursor must never advance before all page writes commit. Retry from the persisted cursor after a crash.
8. **Terminal aggregation:** after a child extraction reaches `Completed`, `Failed`, `Expired`, `Cancelled`, or `NeedsReview`, a separate fenced parent-Run coordinator must determine whether all provider steps are terminal, aggregate counts/failures, and finalize the Run. Do not mark the parent Run complete merely because one extraction finished.
9. **Cancellation:** honor a parent `CancelRequested` state. Remote cancellation is best-effort and idempotent; local cancellation/finalization must still be fenced. A cancellation request must not bypass page transaction boundaries.
10. **Recovery:** startup recovery must not reset live leases. Expired leases are reclaimed through the repository's database-clock claim query; `NeedsReview` is never automatically claimed.

## Integration gaps that must be closed before enabling async providers

- No durable extraction consumer/hosted service is registered yet.
- The current Run worker's immutable provider execution plan and extraction-job lifecycle are not yet connected by a durable parent/child coordinator.
- **Current observable behavior:** the Run worker's async-provider preflight throws `InvalidOperationException`; its outer generic exception handler then calls `FailFencedWithDatabaseClockAsync`. Therefore an async-only provider plan currently fails the parent Run rather than pausing it for extraction. The preflight guard is safe against partial provider I/O, but it is not a waiting/continuation state. The parent/child coordinator must introduce a persisted, fenced waiting/resume lifecycle before any async plan is enabled.
- The worker needs a canonical way to reconstruct the immutable `CrmProspectingProviderRequest` for a child extraction after process restart. It must come from the persisted Run snapshot, not mutable campaign/strategy configuration.
- The current candidate page persistence service enforces extraction-job fencing, but the worker still needs the tenant-scoped duplicate matching and evidence-envelope construction path used by synchronous projection.
- Parent Run finalization must wait for every persisted provider execution/extraction child and must not overwrite `CancelRequested`, terminal states, or a newer fencing generation.
- The worker must have bounded concurrency, bounded page/record memory, cancellation-aware HTTP calls, and an explicit retry/backoff policy. Provider adapters must not write CRM leads directly.
- DI registration and hosted-service lifecycle must follow the framework's `XiHanBackgroundServiceBase` convention; do not register the same consumer twice with both framework discovery and `AddHostedService`.

## Acceptance tests required before removing the async preflight guard

- Submission intent survives a process restart; retries reuse the same idempotency key.
- A timeout after remote acceptance does not create a second remote job.
- Two workers cannot claim the same due job; expired lease tokens cannot renew or write.
- A stale fencing generation cannot acknowledge, transition, advance a cursor, or persist a page.
- Poll status regression and unknown enum values move to `NeedsReview`, never to success.
- A crash after page fetch but before commit re-fetches from the same cursor; a crash after commit resumes from the next cursor without duplicate candidates/evidence.
- Final-page partial failures produce a failed extraction and preserve already committed valid records.
- A parent Run is finalized only after every child execution is terminal; cancellation and tenant isolation are covered.
- PostgreSQL integration tests exercise `SKIP LOCKED`, transaction rollback, cursor CAS, tenant isolation, and lease fencing with at least two independent connections.

## Safe rollout rule

Keep the current Run preflight rejection for asynchronous providers until the consumer, parent coordinator, DI registration, and PostgreSQL integration suite are all present and passing. Do not silently allow mixed synchronous/asynchronous plans before durable parent aggregation exists.

## 2026-11-08 parent Run continuation decision

Source review confirms that `CrmProspectingRunStatus` currently has only `Queued`, `Running`, `Succeeded`, `PartiallySucceeded`, `Failed`, `CancelRequested`, and `Cancelled`. There is no persisted waiting-for-extractions state. The current Run worker throws on async-provider preflight, and its generic exception path persists `Failed`; this is fail-closed but not a resumable async workflow.

Before implementing the consumer, add an explicit parent lifecycle rather than overloading `Queued` or `Running`:

1. Add `WaitingForExtractions` as a non-terminal Run state. It must not be claimable by the synchronous Run worker or counted as completed.
2. Under the current live Run lease and one database transaction, persist all child extraction intents/provider execution rows from the immutable Run snapshot, then transition the parent Run from `Running` to `WaitingForExtractions`. A crash must not leave a parent waiting with missing children or children without a parent transition.
3. Release the Run lease only after the waiting transition commits. Child workers claim extraction jobs independently using their own lease/fencing tuple.
4. Add a parent continuation claim/lease with its own fencing generation. It may resume only after every persisted child is terminal or explicitly `NeedsReview`; it must re-check cancellation and child counts inside the finalization transaction.
5. On child completion, enqueue a durable continuation signal (or let a bounded recovery scanner discover eligible parents). The signal is an optimization only; the database child states remain authoritative.
6. Cancellation from `WaitingForExtractions` must atomically mark the parent `CancelRequested`, signal child cancellation, and prevent a continuation worker from finalizing success. Remote cancellation remains best-effort.
7. Recovery must cover the crash windows between child-intent creation, parent waiting transition, child completion, and parent continuation. Every transition must be conditional on tenant, expected state, lease token, and fencing version.

Required repository/API additions include a fenced `TryWaitForExtractions` transition, a continuation claim/renew/finalize path, and a tenant-scoped query for parents whose complete child set is terminal. Do not implement these as unfenced status assignments.

**Gate:** keep async providers disabled until the new parent state is included in schema/enum serialization, API status handling, queue recovery, cancellation, migration scripts, and PostgreSQL integration tests. This decision record is a design clarification; it does not claim these code changes are implemented.
### 2026-11-08 persisted Run status compatibility note

Source inspection confirms `SysCrmProspectingRun.Status` is persisted as an integer enum, and the repository lease claim currently admits only `Queued` or expired `Running` rows. Adding `WaitingForExtractions` does not require widening the database column, but it is not a one-line enum-only change:

- Reserve a new stable numeric enum value without renumbering existing values.
- Ensure API DTOs, frontend status labels/filters, reporting, and serialization treat it as non-terminal and non-claimable.
- Keep `TryClaimLeaseAsync` restricted to `Queued` and expired `Running`; a waiting parent must never be reclaimed by the synchronous worker.
- Add a fenced transition that writes the complete child execution set and parent waiting state in the same transaction. A status-only transition is forbidden because it can strand the Run without durable children.
- Add a separate parent continuation claim/lease and recovery query before any waiting state can be emitted in production.

No enum or repository change is made in this checkpoint because the transactionally complete parent/child coordinator is not yet present. This avoids introducing a state that can strand live Runs.

### 2026-11-08 async preflight failure semantics

Source review confirms that the async-provider preflight exception currently reaches the Run worker's generic exception handler, which persists the parent Run as `Failed` under the active fencing lease. This is fail-closed (no provider is invoked and no in-memory result is discarded), but it is not a resumable async workflow and must not be presented as one.

Implementation requirement for the next A-line slice:

- Replace the generic preflight exception only after the parent/child transaction and durable continuation path exist.
- The transition into `WaitingForExtractions` must be a dedicated fenced repository operation, not a catch-filter that merely suppresses `Failed`.
- If child-intent creation or the parent waiting transition fails, preserve the current fenced failure behavior; never leave a parent Run `Running` without a valid worker lease or waiting without its complete child set.
- Add tests proving async-only and mixed plans create no synchronous provider ledger/network activity before the atomic wait transition, and that rollback leaves no orphan child intents.

No production behavior is changed in this decision record. Async providers remain unavailable to the Run worker.


### 2026-11-08 parent continuation crash-window matrix

The parent/child hand-off must be implemented and tested against each crash boundary below. The transaction is the only authority; queue messages are hints and must never be used to infer durable completion.

| Crash point | Required durable outcome | Recovery owner |
|---|---|---|
| Before child-intent transaction begins | Parent remains Running under its live Run lease; no child intents exist | Run worker lease recovery |
| During child-intent inserts, before commit | All child intents and parent status change roll back together | Run worker retries under a new valid lease |
| After atomic child-intent + Waiting commit, before queue publish | Parent is WaitingForExtractions with the complete child set | Tenant-scoped parent recovery scanner |
| After child claim, before provider submit | Child remains recoverable by its own lease expiry; submission key is unchanged | Extraction consumer |
| Remote provider accepts submit, response lost | Retry uses the same persisted idempotency key; ambiguous non-idempotent providers are not eligible | Extraction consumer/provider adapter |
| After child terminal commit, before continuation signal | Child is terminal; parent remains waiting until continuation is discovered | Parent recovery scanner |
| During parent aggregation/finalization | Aggregation and terminal transition roll back together unless the exact continuation lease/fencing generation commits | Parent continuation worker |
| Cancellation races finalization | One conditional fenced transition wins; success cannot overwrite CancelRequested/Cancelled | Parent coordinator |

Implementation must keep child-set completeness check, cancellation re-check, aggregate counts, provider-step finalization, and parent terminal transition inside one fenced transaction. A signal/queue publish after commit may be duplicated safely. A missing signal must not strand a parent because recovery scans durable state.

This matrix adds acceptance criteria only; no parent coordinator or WaitingForExtractions state is implemented yet.


### 2026-11-08 Run lease PostgreSQL integration coverage

Added PostgreSQL-gated integration tests using two independent SqlSugar clients and the real AsyncLocal tenant accessor. Coverage includes (1) exactly one concurrent queued-Run claimant, (2) expired-lease takeover with a fresh token and incremented fencing version plus stale-owner renewal rejection, (3) expired CancelRequested finalization to Cancelled without requeue, and (4) explicit legacy Running-with-null-lease recovery. These tests require KIGER_TEST_POSTGRES_CONNECTION_STRING and have not been executed in this environment. They do not implement parent/child extraction continuation.
