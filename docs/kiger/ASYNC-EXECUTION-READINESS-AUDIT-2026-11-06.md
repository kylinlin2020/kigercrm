# Async Prospecting Execution Readiness Audit — 2026-11-06

## Scope

This audit checks whether the asynchronous provider extraction foundation is currently wired into an executable application path. It is a source-level audit of `kiger-dev`, not a runtime validation.

## Verified current state

### Present

- `ICrmProspectingAsyncExtractionProvider` defines Submit, Poll, Fetch-page and Cancel contracts.
- `SysCrmProspectingExtractionJob` persists tenant-scoped lifecycle state, submission idempotency key, remote extraction ID, cursor, counters, lease token and fencing version.
- `ICrmProspectingExtractionJobRepository` exposes durable submission intent, lease claim/renewal, fenced transitions and cursor/result completion operations.
- `CrmProspectingExtractionJobRepository.ClaimDueAsync` uses PostgreSQL `FOR UPDATE SKIP LOCKED`, database time and a monotonically increasing fencing version.
- Pure policy components exist for action selection, state transitions, polling, cursor validation, page validation and submission validation.
- `CrmProspectingExtractionExpiryHostedService` performs tenant-scoped expiry maintenance only. Its source explicitly states that it does not claim jobs or perform provider I/O.
- `CrmProspectingRunHostedService` remains the synchronous Run execution worker and uses the synchronous provider contract.

### Missing executable integration

- No implementation of `ICrmProspectingAsyncExtractionProvider` was found in the CRM infrastructure provider source tree.
- `AddCrmDataSeeders` resolves the async provider registry from `GetServices<ICrmProspectingAsyncExtractionProvider>()`, but the inspected registration code does not register an async extraction adapter.
- No dedicated hosted consumer was found that calls `ClaimDueAsync` and executes Submit/Poll/Fetch.
- No end-to-end application orchestration currently connects extraction-job completion to candidate/evidence/score projection and parent Run resumption.
- The current expiry hosted service must not be mistaken for an async execution consumer.

## Required implementation sequence

1. Add at least one real, opt-in async provider adapter with documented idempotency semantics and tenant-scoped credentials.
2. Add a dedicated tenant-scoped consumer that claims jobs, renews leases, dispatches only the policy-selected action, and fences every state mutation.
3. On Submit, reuse the persisted `SubmissionIdempotencyKey`; ambiguous outcomes must reconcile by that same key or move to `NeedsReview`.
4. On Poll, apply bounded backoff and fail closed for unknown or regressive provider states.
5. On Fetch, validate every page and commit candidate/evidence/score projection plus cursor advancement in one transaction.
6. Add restart, duplicate submission, lease takeover, stale fencing, cross-page deduplication, tenant isolation and parent-Run continuation integration tests.
7. Keep automatic external communications and CRM lead conversion behind existing explicit approval/authorization paths.

## Merge and validation gate

- Do not claim async extraction is operational until an adapter and consumer are registered and an end-to-end test exercises Submit → Poll → Fetch → projection → Run completion.
- GitHub currently reports PR #1 as open and Draft, with `mergeable=false`. The cause has not been diagnosed in this audit; inspect the branch/base comparison before any merge attempt.
- No workflow runs were returned for branch `kiger-dev` in the latest Actions query.
- No .NET build, test, PostgreSQL integration run or Windows real-machine acceptance is claimed by this audit.
- Keep `main` unchanged and retain the Windows/PostgreSQL acceptance gate.
