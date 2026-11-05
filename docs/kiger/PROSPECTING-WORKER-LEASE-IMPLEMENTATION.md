# Prospecting worker lease and fencing implementation design

## Status

Design gate only. This document does not claim that multi-instance execution is implemented or safe. Until the implementation and PostgreSQL concurrency suite pass, deploy exactly one worker-bearing application process as described in [PROSPECTING-WORKER-OPERATIONS.md](./PROSPECTING-WORKER-OPERATIONS.md).

## Verified current behavior

- Startup calls `RecoverPendingAsync` before queue polling.
- Recovery changes every tenant-scoped `Running` Run to `Queued`; there is no owner, expiry, heartbeat, or fencing predicate.
- `ClaimQueuedAsync` conditionally changes `Queued` to `Running`, then reads the row.
- Candidate rows are currently persisted individually. A terminal Run update does not fence candidate writes.
- Redis is a delivery mechanism; it is not authoritative ownership.
- The current Run entity has no lease fields.

These facts are the reason the lease implementation is a correctness prerequisite, not an optional scaling enhancement.

## Required persisted state

Add to `Crm_Prospecting_Run`:

| Field | Suggested type | Meaning |
|---|---|---|
| `Lease_Owner_Id` | varchar(100), nullable | Random process-instance identity, regenerated on process start |
| `Lease_Expires_At` | timestamptz, nullable | Database-clock expiry instant |
| `Fencing_Token` | bigint, not null, default 0 | Monotonically increasing ownership generation |

The fencing token must never be reset when a Run is requeued, reclaimed, cancelled, or retried. A claim increments it atomically and returns the new value to the winner. Owner identity is diagnostic; the token is the stale-owner fence.

Use a migration that is safe for existing rows: nullable owner/expiry and token backfill to zero, then enforce the non-null/default constraint. Confirm naming conventions and fresh-tenant CodeFirst behavior before shipping the migration. Do not assume that an upgrade script automatically runs during fresh tenant provisioning.

## Atomic claim contract

Repository contract should return a claim object containing the Run snapshot, owner ID, and fencing token, or null when no claim was acquired.

The database operation must be one atomic conditional update (or a transactionally equivalent locked-row operation):

1. Match the Run ID and strict tenant scope.
2. Permit claim only when status is `Queued`, or when status is `Running` and the existing lease is expired according to database server time.
3. Increment `Fencing_Token` in the database expression.
4. Set owner, expiry, and status in the same statement.
5. Return the updated row/token from the same operation where supported; otherwise read it in the same transaction while retaining ownership.
6. Use database server time for expiry comparisons and renewal. Application `UtcNow` is not authoritative for lease arbitration.

A zero-row update means claim loss. A Redis message never bypasses this operation.

## Renewal contract

Renewal must condition on all of:

- Run ID and tenant scope;
- `Status = Running`;
- exact `Lease_Owner_Id`;
- exact `Fencing_Token`;
- lease not already expired according to database time.

Renewal must not resurrect an expired lease. A zero-row update means the worker has lost ownership and must stop processing and must not write any further state or candidates.

Heartbeat interval and lease duration must be configurable, with a conservative lease duration greater than the maximum expected scheduling pause plus database round-trip margin. Configuration validation must reject non-positive values and a heartbeat interval that is not safely shorter than the lease duration.

## Fencing every write

Every Run mutation (`Complete`, `Fail`, cancellation acknowledgement, heartbeat) must include the current owner and fencing token in its conditional predicate.

Candidate persistence is the critical boundary. For each candidate batch (or each row until a verified bulk path exists):

1. Begin a database transaction on the same connection used for all statements.
2. Lock or conditionally validate the Run row for matching Run ID, tenant, `Running` status, owner, token, and unexpired lease using database time.
3. If validation fails, abort the transaction and report lost ownership.
4. Insert/update candidate rows in that transaction.
5. Commit.

A read-before-write ownership check is insufficient: lease takeover can happen between the read and insert. The Run-row lock or equivalent write fence must be held through candidate persistence. The implementation must prove that a takeover cannot commit while an old owner's candidate transaction is still allowed to commit.

Do not rely only on checking ownership before calling `AddAsync`, on a process-local mutex, on Redis locks, or on the final Run status update.

## Recovery and cancellation

- Startup recovery must stop blindly changing all `Running` rows to `Queued`.
- Recovery may reclaim only expired leases using the same atomic claim/takeover protocol; it must increment the fencing token before new work starts.
- `CancelRequested` remains an explicit cancellation intent. Cancellation and reclaim must be serialized by conditional updates/row locking so that a stale worker cannot complete after cancellation wins.
- A worker that loses its lease must not mark the Run failed or cancelled; it no longer owns the row.
- Queue rebuild should publish IDs for queued or reclaimable work, but publication itself never grants ownership.

## Framework/API verification gate

Before implementation, verify against the exact XiHan Framework and SqlSugar package versions resolved by this repository:

- How to execute parameterized PostgreSQL SQL without bypassing tenant scope.
- How to acquire a row lock (`SELECT ... FOR UPDATE`) on the same connection/transaction as repository writes, or an equivalent atomic conditional update.
- How the framework UnitOfWork exposes the transaction and connection to multiple repositories.
- Whether returned rows from `UPDATE ... RETURNING` can be mapped safely.
- Whether server-side timestamp expressions are translated consistently.
- How transaction rollback behaves when ownership validation fails.

Do not invent a SqlSugar method name or assume an application service UnitOfWork attribute covers hosted-service repository calls. If the framework abstraction cannot express the required atomicity, add a narrowly scoped PostgreSQL-specific lease store with explicit parameterization, tenant predicates, and integration tests rather than weakening the invariant.

## Required PostgreSQL independent-connection/process tests

The tests must use independent database connections; SQLite source tests are only preflight checks.

1. Two concurrent initial claims: exactly one winner and one fencing token generation.
2. A live lease prevents a second worker from claiming.
3. Expired lease takeover succeeds with a strictly higher fencing token.
4. Stale owner cannot renew, complete, fail, acknowledge cancellation, or write a candidate after takeover.
5. Renewal racing expiry/takeover has one serialized outcome.
6. Cancellation racing reclaim cannot be overwritten by stale completion.
7. Candidate transaction and takeover race proves the old owner cannot commit candidate writes after losing ownership.
8. Process-death simulation followed by expiry permits recovery.
9. Tenant A cannot claim, renew, reclaim, or write under tenant B's Run.
10. Migration tests cover existing queued/running/cancel-requested/terminal rows, soft-deleted rows, upgrade path, and fresh-tenant provisioning.

## Delivery order

1. Pin exact framework/package versions and prove transaction/locking APIs with a minimal integration test.
2. Add entity fields and upgrade/fresh-provisioning migration coverage.
3. Implement atomic claim, renewal, takeover, and fencing predicates.
4. Make candidate writes transactional and fenced.
5. Replace broad startup recovery and add queue rebuild behavior.
6. Add PostgreSQL race tests and failure-injection tests.
7. Add multi-instance deployment/rolling-restart tests.
8. Only after all gates pass, revise the runbook to permit multiple workers.

## Acceptance boundary

Passing source-wiring tests or SQLite tests does not authorize multiple workers. Multi-instance support requires the PostgreSQL independent-connection suite and deployment overlap tests to pass, followed by Windows pull/build/runtime acceptance. Until then, the single-active-worker restriction remains mandatory.


## Framework 4.8.0 source verification (2026-11-04)

The application references `XiHan.Framework.Web.*` packages at version `4.8.0` in `backend/src/framework/XiHan.BasicApp.Web.Core/XiHan.BasicApp.Web.Core.csproj`; the backend targets `net10.0`. The following findings were checked against the public `XiHanFun/XiHan.Framework` source tag `v4.8.0`, not inferred from method names:

### Confirmed usable primitives

- `IUnitOfWorkManager.Begin(XiHanUnitOfWorkOptions, requiresNew)` starts a framework unit of work.
- `IUnitOfWork.CompleteAsync` commits registered transaction APIs; `RollbackAsync` rolls them back; `Dispose` releases the scope.
- `SqlSugarClientResolver` enlists the resolved client in the current transactional UOW and pins the concrete `SqlSugarProvider` connection by ConfigId. Repository calls in that UOW therefore share the same physical connection/transaction. The resolver explicitly documents sequential access; concurrent operations on that shared connection are unsupported.
- `SqlSugarTransactionApi` opens a transaction through `Ado.BeginTran` and commits/rolls back through the same concrete client.
- `SqlSugarRepositoryBase.UpdateAsync(columns, predicate)` issues a conditional update and returns whether affected rows are greater than zero. It also appends the strict tenant write-scope predicate. This is a verified primitive for compare-and-swap style operations, not proof of server-time or lease correctness.
- The CRM project already uses the 4.8.0 framework line via its Saas/Web.Core project references.

### Not yet verified; do not implement by assumption

- A public repository-safe API for PostgreSQL `SELECT ... FOR UPDATE` on the exact pinned connection.
- A safe, supported way to execute parameterized PostgreSQL-specific SQL/expressions through the repository's pinned client while preserving tenant predicates and transaction enlistment.
- A portable SqlSugar expression for database-server current time and atomic `Fencing_Token = Fencing_Token + 1` returning the winning token.
- Mapping semantics for PostgreSQL `UPDATE ... RETURNING` through the framework's configured client.
- Whether the framework's generic conditional update expression supports arithmetic against the current database column (rather than a value calculated in application memory).

### Implementation decision

Do not start lease entity/repository implementation until the remaining PostgreSQL primitives above are proven with a minimal integration test using the actual resolved package graph and PostgreSQL. The safe next step is to inspect the framework's SqlSugar transaction API and supported raw-Ado patterns in source, then create a narrow PostgreSQL lease-store proof that obtains the client through `ISqlSugarClientResolver` inside an explicit `IUnitOfWorkManager.Begin(...)` scope. Any raw SQL must be parameterized, tenant-scoped, and executed on that same enlisted client. If this cannot be proven, stop and document the framework limitation rather than weakening the fencing invariant.

These source checks are not a build, runtime test, PostgreSQL integration test, or Windows acceptance test.
