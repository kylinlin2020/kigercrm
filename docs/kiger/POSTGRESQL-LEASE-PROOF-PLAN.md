# PostgreSQL lease proof plan

## Status and scope

Planning artifact only. No PostgreSQL fixture, lease implementation, or PostgreSQL test has been added by this document. The existing SQLite repository tests remain useful for deterministic repository behavior, but they cannot prove PostgreSQL row-locking, server-clock arbitration, independent-connection races, or transactional candidate fencing.

The current `CrmProspectingRunRepository` still:
- claims by conditional `Queued -> Running` update followed by a read;
- recovers every tenant-scoped `Running` row to `Queued` without checking process ownership or expiry;
- completes/fails using status-only predicates;
- has no persisted lease owner, expiry, or fencing token.

Do not enable multiple worker-bearing application processes.

## Existing test-project facts

- Test project: `backend/test/XiHan.BasicApp.Api.Tests`.
- Target framework is inherited from shared props; backend projects currently target `.NET 10`.
- Tests use xUnit v3, Moq, Microsoft.NET.Test.Sdk, and real SqlSugar/SQLite integration fixtures.
- `test.props` currently has no PostgreSQL Testcontainers or direct Npgsql package references.
- Root `docker-compose.yml` is the application stack, not an isolated disposable test fixture. The lease tests must not reuse its developer database.

Before adding dependencies, inspect the resolved package graph and choose mutually compatible, explicitly pinned versions for the PostgreSQL driver and Testcontainers PostgreSQL module. Do not rely on an incidental transitive Npgsql reference.

## Fixture isolation and safety

Use a disposable PostgreSQL 17 container dedicated to the test process (or a CI service container with a unique database/schema per test run). Never accept an arbitrary developer/production connection string as the default.

Required safeguards:
1. Start a fresh container with a randomly generated database name and strong ephemeral credentials.
2. Require an explicit test-only opt-in if a non-container connection is ever supported; reject production-like hosts and databases.
3. Verify server identity/version and database name before creating or dropping anything.
4. Use a unique schema/database namespace per test collection and clean up only objects created by that fixture.
5. Keep tests independent of the root compose volume and application database.
6. Ensure parallel tests do not share one mutable tenant scope or one SqlSugar connection.
7. CI must publish logs and test results on failure, while never printing credentials.

## Prove framework transaction enlistment first

The first test slice must be deliberately small and must run against the exact package graph resolved by this repository:

1. Resolve the tenant-specific SqlSugar client through the framework's `ISqlSugarClientResolver`.
2. Begin an explicit framework UnitOfWork.
3. Execute a parameterized PostgreSQL statement using the same resolved client and verify it participates in the UOW transaction.
4. Roll back the UOW and verify from a second independent connection that the write is absent.
5. Repeat with commit and verify the write is visible from the independent connection.
6. Verify strict tenant predicates are applied to all lease reads and writes; do not treat a raw SQL call as tenant-safe merely because it uses the resolved client.

If the framework public abstraction cannot safely expose parameterized SQL and row locking on the pinned transaction connection, stop and implement a narrow PostgreSQL-specific lease store. That store must receive the tenant ID explicitly, parameterize all values, use the same enlisted connection/transaction as candidate writes, and have focused tests. Do not invent SqlSugar API names or silently open a second connection inside an application transaction.

## Required PostgreSQL behavior tests

Use independent physical connections for competing actors. Synchronize race tests with barriers/latches rather than sleeps wherever possible.

### A. Claim and fencing token

- Two concurrent initial claims for one queued Run: exactly one succeeds.
- The winning claim receives the persisted fencing token and owner identity.
- A second claim while the lease is live fails.
- After server-clock expiry, exactly one takeover succeeds.
- Takeover increments the fencing token in the database; it must be strictly greater than the previous token.
- A worker cannot claim or mutate another tenant's Run.

### B. Renewal and terminal transitions

- Renewal succeeds only for the current tenant, owner, token, Running state, and unexpired lease.
- Renewal after expiry does not resurrect the old lease.
- Old owner cannot renew, complete, fail, or acknowledge cancellation after takeover.
- Renewal racing expiry/takeover has one serialized outcome.
- Cancellation racing reclaim cannot be overwritten by stale completion.
- A worker that has lost ownership cannot mark the Run failed or cancelled.

### C. Candidate write fencing (critical)

Create a deterministic race:
1. Worker A starts a transaction and attempts candidate persistence for token N.
2. Hold the Run ownership row lock/fence while A is inside that transaction.
3. Worker B attempts takeover from a separate connection.
4. Prove B cannot take ownership while A's fenced candidate transaction is active.
5. Commit A; then allow B to acquire a higher token.
6. In a second race, let B take over first, then prove A with token N cannot insert or commit any candidate rows.
7. Verify Run state and candidate rows from a third independent connection after both actors finish.

The production contract must hold the ownership lock or equivalent write fence through the candidate insert and transaction commit. A read-before-write check is not sufficient. Until a verified bulk insert exists, apply the same fence to every individual candidate insert transaction.

### D. Recovery, cancellation, and process death

- Startup recovery does not requeue a live lease.
- Expired lease can be reclaimed with a higher token.
- Process-death simulation (terminate actor without releasing lease) permits recovery only after database-clock expiry.
- CancelRequested is preserved as cancellation intent and cannot be converted into queued work.
- Queue publication does not grant ownership; every dequeued ID must pass the database claim.

### E. Migration compatibility

Test both upgrade and fresh-tenant provisioning:
- Existing Queued, Running, CancelRequested, Succeeded, Failed, and Cancelled rows.
- Soft-deleted rows.
- Existing tenants with no rows and with historical rows.
- Lease owner/expiry nullable migration and token backfill/default/non-null behavior.
- Fresh CodeFirst tenant creation includes all required columns and indexes.
- Upgrade path and fresh provisioning converge to the same schema.

## Suggested implementation order

1. Add the isolated PostgreSQL fixture and prove framework UOW/connection enlistment.
2. Prove parameterized SQL, database server time, row lock semantics, and atomic token increment/return.
3. Add the lease entity fields and migration/fresh-provisioning coverage.
4. Implement claim, renewal, takeover, and all terminal state predicates.
5. Fence candidate persistence in the same transaction as ownership validation.
6. Replace broad startup recovery with expiry-aware recovery; Redis remains a queue hint only.
7. Add all independent-connection races and failure-injection cases above.
8. Run the full backend suite and CI; then perform Windows native pull/build/run acceptance.
9. Only after all gates pass may the operations guide permit multiple worker instances.

## Acceptance record

For each test run, record:
- commit SHA;
- PostgreSQL server version;
- resolved .NET SDK and package versions;
- test command and exit code;
- test count and failed/skipped count;
- whether tests used independent physical connections;
- whether migration tests covered both upgrade and fresh tenant provisioning.

A passing SQLite test, source inspection, successful compilation, or clean PR mergeability is not a substitute for these PostgreSQL proofs. Windows real-machine acceptance remains a separate final gate.
