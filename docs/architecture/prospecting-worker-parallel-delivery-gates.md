# Prospecting Worker: Parallel Delivery Gates

**Branch:** `kiger-dev`  
**Status:** atomic claim primitive implemented but not PostgreSQL-verified; no lease activation authorized.

This checklist separates the workstreams so implementation can proceed independently without accidentally enabling unsafe worker concurrency.

## Workstream A — Atomic lease acquisition

- [x] Add a PostgreSQL-only repository operation that performs one conditional `UPDATE ... RETURNING` (`TryClaimLeaseAsync`; not wired into the HostedService).
- [x] Resolve the client through inherited `DbClient` so the active tenant's transaction-aware SqlSugar client is reused.
- [x] Bind `tenant_id`, Run ID, status, owner, new UUID token, and lease duration as parameters; use PostgreSQL `clock_timestamp()` for authoritative time.
- [x] Increment `fencing_version` in the UPDATE expression, not in application memory.
- [x] Return the row produced by RETURNING; never follow with a SELECT to discover the winning token.
- [x] Fail closed for non-PostgreSQL providers; SQLite's current repository tests are not a substitute.
- [ ] Compile against the actual resolved SqlSugarCore 5.1.4.221 package and prove the ADO command enlists in the active UnitOfWork transaction.

## Workstream B — Lease lifecycle

- [ ] Add a stable per-process worker identity (bounded to the 160-character column).
- [ ] Generate a cryptographically random token per claim attempt.
- [x] Add a repository heartbeat primitive that renews only an unexpired Running lease with matching tenant, Run ID, token, and version using PostgreSQL database time.
- [ ] Wire the database-clock heartbeat into the HostedService and stop provider/persistence work immediately when renewal fails.
- [ ] Reclaim only expired leases; never blanket-requeue every Running row once leases are active.
- [ ] Ensure CancelRequested remains cancellation intent and cannot be silently requeued.
- [ ] Clear active lease metadata on terminal transitions while retaining FencingVersion.
- [ ] Stop all further persistence immediately after a failed heartbeat/fence check.

## Workstream C — Child-write transaction fence

The existing Candidate/Evidence/Score write path is not yet protected by the lease guard. A repository primitive, `TryLockLeaseForWriteAsync`, now requires an active transactional UnitOfWork and executes a tenant/token/version/expiry-checked `SELECT ... FOR UPDATE` using database time. It is not called by the HostedService yet. A pre-read is insufficient.

- [ ] Begin one explicit transactional UnitOfWork.
- [x] Add a repository guard that verifies/locks the Run row using the active tenant-scoped client and exact token/version; it fails closed without an active ADO transaction.
- [ ] Call the guard before every Candidate/Evidence/Score insert or repair, using the same UnitOfWork and lease identity.
- [x] Guard checks that the lease is still live using PostgreSQL database time.
- [ ] Integrate the guard into every child persistence transaction; the HostedService currently does not call it.
- [ ] Verify takeover and child-write serialization with two independent PostgreSQL connections.
- [ ] Insert/repair Candidate, Evidence, and initial Score in that same transaction.
- [ ] Roll back all child writes if the Run lease check fails or any child persistence fails.
- [ ] Keep existing Candidate review/approval/conversion fields untouched during provenance repair.

## Workstream D — PostgreSQL integration tests

Tests must use separate physical connections and independent scopes. A single SqlSugar client or SQLite in-memory database cannot prove multi-worker race behavior.

Required cases:

1. Two workers race for one queued Run; exactly one receives a row.
2. Expired lease takeover changes token and increments fencing version exactly once.
3. Previous token cannot renew, complete, fail, cancel, or write children after takeover.
4. Tenant A cannot claim or mutate Tenant B's Run.
5. A transaction that loses the fence rolls back Candidate, Evidence, and Score writes.
6. A process crash leaves a lease that becomes reclaimable only after expiry.
7. CancelRequested cannot be converted back into executable work by recovery.

The test fixture must be explicitly opt-in through an environment variable and must never target a production database. It should create an isolated schema/database and clean it up only when the fixture itself created it.

## Workstream E — Windows-native acceptance

- [ ] Pull `kiger-dev` on Windows without WSL/Docker.
- [ ] Restore the solution with the repository's pinned SDK and NuGet configuration.
- [ ] Build the CRM module and API test project.
- [ ] Run the existing SQLite repository tests.
- [ ] Run PostgreSQL tests against a disposable local PostgreSQL instance.
- [ ] Apply migrations to a disposable database and verify schema/index definitions.
- [ ] Run one end-to-end approved prospecting Run and inspect Candidate/Evidence/Score persistence.
- [ ] Verify email sending and website publishing remain manual-approval-only.

## Activation rule

Until Workstreams A–D are implemented and tested, retain the single-active-application-instance restriction. A new `TryClaimLeaseAsync` primitive now performs the database-atomic claim, but the HostedService still calls legacy `ClaimQueuedAsync`. The new primitive is not active until ADO transaction enlistment, PostgreSQL concurrency tests, heartbeat, child-write fencing, recovery, and terminal transitions are complete. The current `RecoverInterruptedAsync` blanket-requeues Running rows and must not be used as multi-instance lease recovery.

The new source-level assertions only guard SQL/transaction shape; they are not provider-backed tests. A green SQLite test suite, a source-level assertion, or a successful build alone does not authorize multi-instance deployment.
