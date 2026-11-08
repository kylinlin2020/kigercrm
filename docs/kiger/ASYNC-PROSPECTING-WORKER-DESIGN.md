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