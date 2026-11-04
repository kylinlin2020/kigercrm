# Prospecting Worker Atomic Persistence Decision

Status: implementation and verification gate
Branch: `kiger-dev`
Scope: Candidate + Evidence + Score persistence for automatic prospecting
Last audited: 2026-11-04

## Current implementation

The Worker now persists eligible Candidate, Evidence, and the first Score snapshot together in an explicit transactional UnitOfWork for each newly accepted raw envelope. It also repairs missing Evidence and a missing first Score snapshot for an existing Candidate in a separate explicit transaction. Provider network calls and Run-wide normalization remain outside those short database transactions.

The Worker obtains `IUnitOfWorkManager` from the active DI scope and calls `Begin(new XiHanUnitOfWorkOptions(isTransactional: true))`; it awaits all three repositories before `CompleteAsync`. This is implemented source behavior. It is not yet a substitute for proving the exact framework enlistment and rollback behavior against the deployed XiHan.Framework 4.8.0 runtime and PostgreSQL.

### New Candidate path

1. Normalize Candidate data and build Evidence from the original `CrmProspectingRawEnvelope` before opening the transaction.
2. Begin a short transactional UnitOfWork.
3. Insert Candidate and use its generated `BasicId`.
4. Insert eligible Evidence rows.
5. Evaluate and insert the conservative first Score snapshot. Unknown procurement, organization verification, product-fit, and market-fit facts remain false rather than inferred.
6. Complete the UnitOfWork.
7. Only after successful completion, update in-memory identity sets and counters.

### Existing Candidate repair path

1. Reload persisted Evidence and compare the complete Evidence identity components used by the unique index.
2. Read the latest Score history and determine whether the initial Score snapshot is absent.
3. If Evidence or Score is missing, begin a short transaction.
4. Insert only the currently missing Evidence and/or the first Score snapshot.
5. Complete the UnitOfWork.

This repairs legacy partial rows and retries after a prior process interruption. It does not yet recover automatically from a concurrent unique-key collision.

## Transaction and conflict boundary

A database uniqueness violation can abort the current transaction. The Worker must not catch such an exception inside the failed UnitOfWork and continue querying or inserting through the same transaction/connection.

Current behavior on a persistence exception is to let it leave the per-record transaction and reach the Worker's outer failure handler. The handler marks the Run Failed (unless cancellation takes precedence). Startup recovery requeues Running Runs, not Failed Runs. This is deliberately fail-closed, but it means an Evidence collision is not yet an automatically recovered idempotent success.

Required implementation before claiming automatic collision recovery:

1. Let the failed UnitOfWork fully dispose and roll back.
2. Classify the collision narrowly as the Evidence identity unique constraint. Do not treat every SqlSugar/database exception as a duplicate.
3. Start a fresh UnitOfWork/scope and reload rows under the tenant-scoped Candidate.
4. Compare the exact identity tuple: TenantId, CandidateId, ProviderCode, ProviderRecordId, FieldName, SourceUrlHash, and RawPayloadHash. The first two are the tenant/Candidate scope; the remaining five are the Evidence natural identity.
5. Treat the collision as idempotent only when every expected row is now present with the exact identity.
6. Re-check Score history because the failed transaction may have rolled back both Evidence and Score.
7. Insert only still-missing rows in a new transaction, with a small bounded retry count. Propagate unrelated database failures.
8. Add independent-connection PostgreSQL tests for the actual race and recovery behavior.

The SQLite evidence integration suite now verifies exact duplicate rejection, identity dimensions, tenant separation, and a rollback-before-reconciliation preflight. These are test sources only until executed. The rollback preflight does not implement automatic recovery and does not prove PostgreSQL's aborted-transaction behavior.

## Candidate identity and partial retry behavior

Candidate uniqueness is tenant- and Run-scoped over the provider/external-record identity. The Worker uses a typed `(ProviderCode, ExternalId)` tuple with ordinal, case-sensitive equality to mirror the database identity semantics.

- In-memory identity sets are optimizations, not concurrency guarantees.
- Candidate uniqueness does not by itself make Evidence/Score writes idempotent.
- The current per-record transaction prevents a newly inserted Candidate from being committed without its eligible Evidence/first Score when that transaction fails.
- Existing Candidate repair is useful for older or partial rows, but its concurrent unique-key collision still fails the Run as described above.
- The same provider identity may appear in separate Runs unless a separate cross-Run duplicate policy is introduced.

## Score and evidence policy

- Evidence is built from the original provider envelope, not reconstructed from normalized Candidate fields.
- Ingestion is not verification; newly ingested Evidence remains unverified.
- Unsafe or missing source URLs do not produce traceable Evidence.
- Do not hash low-entropy contact names or email addresses without an appropriate keyed scheme.
- Do not infer procurement intent, product fit, market fit, or verified organization identity from generic summaries.
- A Score or priority never approves a Candidate, converts a Lead, sends outreach, or publishes website content.
- Human review remains required before a Candidate enters the formal CRM Lead/customer workflow. External email and website publishing retain their separate manual approval gates.


## Concurrent identity collision recovery: current gap and safe design

### Run RequestKey

Current `CreateRunAsync` behavior is:

1. Build the immutable execution snapshot and SHA-256 `PayloadHash`.
2. Query the tenant-scoped Run repository for `RequestKey`.
3. If found, return it only when its `PayloadHash` matches; otherwise reject reuse.
4. If absent, insert a Queued Run.

The tenant-scoped unique index on `(tenant_id, request_key)` is the final duplicate-prevention boundary. Two concurrent requests can both observe no row; one insert wins and the other receives a database uniqueness failure. The losing request is not yet guaranteed to return the winner.

Safe completion requires a repository operation that reads by RequestKey under the current tenant and a retry boundary that runs after the failed insert transaction has ended. The recovery must compare the winner's PayloadHash using ordinal equality. Matching hash returns the winner; different hash preserves the existing conflict error. If no winner is visible, propagate the original persistence error. Do not catch every database exception and suppress it merely because a row with that key happens to exist.

Before implementing, establish whether the application's `[UnitOfWork]` interception wraps this domain-service call and whether the framework supports an independent/fresh UnitOfWork after a failed nested operation. A read in an aborted PostgreSQL transaction is not a valid recovery query. Prefer an explicit application-level retry boundary outside the failed UOW, or a database-native insert-on-conflict-returning operation with a provider-neutral contract and tested provider implementations.

Required tests:
- Sequential same key + same payload returns the original Run.
- Sequential same key + different payload rejects.
- Concurrent same key + same payload from independent connections yields exactly one row and both callers receive the same Run identity.
- Concurrent same key + different payload yields exactly one row; the losing caller receives the payload conflict, not the raw unique exception and not the winner as a false success.
- Tenant isolation: the same RequestKey in different tenants remains independent.
- Non-unique database errors are propagated unchanged.

### Candidate identity

The Worker currently snapshots existing Candidates for the Run, then uses an in-memory tuple set `(ProviderCode, ExternalId)` to avoid duplicate work within that Run. The database unique index on tenant + Run + provider + external ID is the cross-worker boundary. Two workers/processes can both miss the in-memory/persisted row and race to insert.

The per-record transaction correctly keeps a newly inserted Candidate, its eligible Evidence, and its first Score atomic. However, the losing Candidate insert currently fails the Run; it does not yet reload the winner and reconcile missing Evidence/Score.

Safe recovery requirements:
1. The failed Candidate transaction must fully roll back and dispose before reconciliation.
2. In a fresh tenant-scoped UOW, reload by the exact tuple `(RunId, ProviderCode, ExternalId)`.
3. Verify the returned Candidate belongs to the expected tenant and Run; do not match on email, website, company name, or fuzzy identity.
4. Reconcile Evidence against the complete Evidence natural key and add a missing first Score snapshot if absent.
5. If the Candidate does not exist after the fresh read, propagate the original database error.
6. Do not overwrite review status, duplicate decision, or conversion state on the winning Candidate.
7. Bound retries and keep all child-row repair writes transactional.

Required independent-connection PostgreSQL tests:
- Two writers race to create the same Candidate identity; one row remains.
- The losing writer reloads and repairs Evidence/Score without changing Candidate status.
- A different ExternalId creates a separate Candidate.
- A different Run creates a separate Candidate.
- Tenant isolation is preserved.
- A failure inserting Evidence or Score rolls back only the losing transaction and does not damage the winner.

### Implementation gate

Do not claim Candidate or Run concurrent creation is fully idempotent until the above service-level race tests pass on PostgreSQL using independent connections/scopes. SQLite unique-index tests are useful preflight checks but are not substitutes for PostgreSQL transaction-abort and concurrency semantics. Keep the Worker single-instance restriction until database lease/fencing is implemented; recovery logic must not be used to justify multi-instance execution.

## PostgreSQL acceptance matrix

The integration suite must run against PostgreSQL with independent connections (and independent scopes for race tests):

| Scenario | Required assertion |
|---|---|
| Normal write | Candidate + eligible Evidence + first Score are visible after commit |
| Evidence insert failure | Candidate and partial Evidence are rolled back |
| Score insert failure | Candidate and Evidence are rolled back |
| Cancellation before commit | No partial record set remains |
| Existing Candidate repair | Only missing Evidence/Score is added |
| Concurrent Evidence identity insert | One writer wins; loser reloads in a fresh transaction and confirms exact identity before treating it as idempotent |
| Concurrent Candidate identity insert | Exactly one Candidate wins; loser reloads the winner and reconciles child rows |
| Separate Runs | Same provider identity may exist in both Runs unless a deliberate cross-Run rule is added |
| Tenant isolation | Tenant A cannot read, attach, or mutate Tenant B Evidence/Score |
| Missing/unsafe source | No Evidence and no evidence-quality points |
| Review read API | Evidence/Score is returned only after campaign owner/data-scope authorization |

SQLite tests are useful repository preflight checks but do not prove PostgreSQL transaction, locking, or concurrency behavior.

## Run creation and worker ownership boundaries

Run creation checks the tenant-scoped RequestKey before insert and validates that a repeated key has the same payload hash. A concurrent same-key insert is still protected by the unique index, but the losing request is not yet guaranteed to reload and return the winning Run. Add narrow conflict recovery and an independent-connection PostgreSQL test before describing concurrent Run creation as fully idempotent.

Keep exactly one active Worker deployment until database-authoritative lease/fencing is implemented. The current startup recovery broadly requeues Running Runs and has no lease owner, heartbeat, expiry, or fencing token. Process-local concurrency limits and conditional Queued-to-Running claim are not distributed ownership.

## Separate validation gates

Report these independently; do not infer one from another:

1. Source inspection and source-level regression test coverage.
2. .NET restore/build.
3. Unit and SQLite integration tests.
4. PostgreSQL migrations and independent-connection integration tests.
5. Windows native pull, build, startup, and runtime acceptance.

As of this audit, no current CI workflow run has been observed for the latest branch head. The newly added SQLite tests have not been executed in this environment. PostgreSQL migrations/concurrency tests and Windows native acceptance remain open. PR #1 remains Draft and must not be merged before the required gates pass.

## 2026-11-04 initial application UnitOfWork audit (historical)

Before the recovery refactor, `StartRunAsync` carried `[UnitOfWork(true)]`, called `domain.CreateRunAsync`, and registered queue publication through the current UoW's `OnCompleted` callback. `CreateRunAsync` performed the tenant-scoped RequestKey lookup before `runs.AddAsync`; the repository lookup used `CreateQueryable()` to retain tenant filtering.

That original structure meant a unique-key exception occurred inside the method's intercepted transaction. Catching it inside the domain method and immediately re-reading through the same transaction was unsafe for PostgreSQL. The exact-version framework audit below established the supported recovery boundary and superseded that initial fail-closed implementation.

The current implementation removes the action-level UoW attribute from `StartRunAsync` and owns the transaction explicitly in a private helper. Broad `SqlSugarException` catches and reads inside an aborted transaction remain prohibited.


Additional HTTP-boundary verification:

- In XiHan.Framework `v4.8.0`, `XiHanUnitOfWorkFilter` starts an action UoW only when the resolved action method has a `UnitOfWorkAttribute`. The updated public `StartRunAsync` deliberately has no such attribute; therefore the HTTP action filter does not create an outer transaction for this endpoint.
- The public method invokes a private helper that manually owns a root transactional UoW. Its `using` scope completes or disposes before the public catch filter handles the exact unique violation. The retry therefore begins after the failed UoW has released/rolled back its transaction, rather than becoming a child UoW.
- This matters because a normal nested `Begin(options)` would return a `ChildUnitOfWork` and reuse the failed parent; it is not suitable for this recovery path.

## 2026-11-04 Run RequestKey recovery implementation

The XiHan.Framework source was checked at the exact `v4.8.0` tag used by this repository (Core project PackageReferences are `4.8.0`):

- `IUnitOfWorkManager.Begin(options, requiresNew: true)` creates a new logical UoW; when an outer UoW exists, `UnitOfWorkManager` marks it `RequiresIsolatedConnection`.
- The SqlSugar resolver honors that flag by calling `CopyNew()`, starting a transaction on the isolated client, and owning/disposal of that client. The transaction adapter fails closed if the supposedly isolated connection already has a transaction.
- `UnitOfWork.Dispose` rolls back unfinished owned transactions; the SqlSugar adapter also rolls back unfinished transactions on disposal.
- The UoW interceptor's ordinary nested path uses a child UoW, which delegates to the parent and is not an independent transaction. Do not use that path for race recovery.

Based on this exact-version audit, Run creation now owns its transaction explicitly in `StartRunInUnitOfWorkAsync`. The helper starts its transaction with `Begin(..., requiresNew: true)`, guaranteeing a distinct logical UoW and, when an outer UoW exists, a distinct physical SqlSugar connection. The public `StartRunAsync` wrapper catches only a PostgreSQL unique violation whose SQLSTATE is `23505` and whose constraint name is exactly `ux_crm_prospecting_run_te_rq`. The helper's `using` scope has disposed/rolled back before the exception reaches the wrapper. It then retries once in another fresh UoW; the domain service performs the tenant-scoped RequestKey lookup first and compares the existing `PayloadHash` before deciding whether to return the winner or reject a different payload.

Queue publication remains an `OnCompleted` callback inside each successful UoW, so it cannot run before commit. A same-payload race may enqueue the winning Run more than once; this is safe only because worker claiming is conditional and duplicate queue deliveries do not independently transition a Run from Queued more than once. Keep the claim predicate and this behavior covered by tests.

Failure policy:
- Non-PostgreSQL providers, unknown wrappers, other SQLSTATEs, and any other constraint name fail closed and propagate the original error.
- A same RequestKey with a different PayloadHash must remain a conflict; it must never return the winner as if the payloads matched.
- Recovery is bounded to one retry; no unbounded loops or broad database-exception catches.
- PostgreSQL independent-connection concurrency tests and Windows build/test remain required before calling this production-verified.

## 2026-11-04 Candidate identity race recovery implementation

The worker now catches only PostgreSQL SQLSTATE `23505` for the exact candidate identity index `ux_crm_prospecting_candidate_te_ru_pr_ex_is`, and only after the candidate/evidence/score insertion UoW has left its `using` scope. It then resolves the winner with `GetByIdentityAsync(runId, providerCode, externalId)`, which uses the tenant-filtered repository query.

On a visible winner, the worker runs the same provenance repair used for legacy/existing candidates:
- Recompute expected Evidence and compare the full persisted identity tuple (provider code, provider record ID, field name, source URL hash, raw payload hash).
- Insert only missing Evidence.
- Add a first Score snapshot only when no Score exists.
- Keep Evidence additions and Score repair in one transaction.
- Never update Candidate review, duplicate, approval, conversion, or other lifecycle fields.

If the exact winner is not visible after the unique violation, the original exception is rethrown. Other providers, SQLSTATEs, and constraint names are not caught. This is a bounded fail-closed recovery, not a substitute for PostgreSQL multi-connection tests. Evidence-index collisions during simultaneous legacy-repair operations and duplicate Score creation under multiple active workers remain separate concurrency cases; the worker must remain single-instance until lease/fencing is implemented.

Evidence reconciliation now also has a single bounded retry for SQLSTATE `23505` on the exact immutable index `ux_crm_prospecting_evidence_evidenceidentity`. The failed repair UoW is disposed before the helper rereads persisted evidence and retries once. Repeated collisions and all unrelated failures propagate. This handles concurrent insertion of the same evidence identity during repair; it does not solve duplicate first-Score snapshots if multiple workers concurrently repair a legacy Candidate with no Evidence and no Score. Single-active-worker operation remains mandatory until a score idempotency/lease design is implemented.

Worker Candidate and provenance repair transactions also now use `Begin(..., requiresNew: true)`. This prevents an ambient UoW (if introduced by a future worker host wrapper) from silently turning the short persistence transaction into a `ChildUnitOfWork` that merely delegates to an outer transaction. Under XiHan.Framework 4.8.0, an existing outer UoW causes the manager to request an isolated physical SqlSugar connection for these short writes.
