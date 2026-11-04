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
- [x] Generate a fresh UUID token per claim attempt in the atomic claim statement.
- [x] Add a repository heartbeat primitive that renews only an unexpired Running lease with matching tenant, Run ID, token, and version using PostgreSQL database time.
- [ ] Wire the database-clock heartbeat into the HostedService and stop provider/persistence work immediately when renewal fails.
- [x] Add a PostgreSQL-only recovery primitive that requeues expired Running leases and finalizes expired CancelRequested leases as Cancelled; both require non-null expired leases, clear active lease metadata, and leave lease-less legacy rows untouched.
- [ ] Wire safe expired-lease recovery into startup recovery; keep legacy blanket recovery disabled once leases are active.
- [x] Ensure expired CancelRequested leases are finalized as Cancelled rather than silently requeued; live cancellation remains owned by the current lease holder.
- [x] Add database-clock fenced Complete/Fail/Cancel terminal primitives that clear active lease metadata while retaining FencingVersion.
- [ ] Stop all further persistence immediately after a failed heartbeat/fence check.

## Workstream C — Child-write transaction fence

The existing Candidate/Evidence/Score write path is not yet protected by the lease guard. A repository primitive, `TryLockLeaseForWriteAsync`, now requires an active transactional UnitOfWork. It first materializes a tenant/token/version-scoped `SELECT ... FOR UPDATE`, then checks expiry with `clock_timestamp()` after acquiring the row lock so lock wait time cannot make an already-expired lease appear live. It is not called by the HostedService yet. A pre-read is insufficient.

- [ ] Begin one explicit transactional UnitOfWork.
- [x] Add a repository guard that verifies/locks the Run row using the active tenant-scoped client and exact token/version; it fails closed without an active ADO transaction.
- [ ] Call the guard before every Candidate/Evidence/Score insert or repair, using the same UnitOfWork and lease identity.
- [x] Guard checks that the lease is still live using PostgreSQL database time after row-lock acquisition.
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
7. Expired CancelRequested leases become Cancelled, never Queued; live cancellation remains fenced to the current holder.

The test fixture must be explicitly opt-in through an environment variable and must never target a production database. The current test project does not yet contain the two-connection PostgreSQL fixture; no PostgreSQL test should be inferred from the new source-level tests. It should create an isolated schema/database and clean it up only when the fixture itself created it.

## Workstream E — Windows-native acceptance

- [ ] Pull `kiger-dev` on Windows without WSL/Docker.
- [x] Add `backend/scripts/acceptance/Invoke-ProspectingWorkerAcceptance.ps1`; it requires a clean `kiger-dev` checkout and .NET 10, then restores/builds the CRM module and API test project and runs prospecting tests (unless `-SkipTests` is supplied).
- [ ] Execute the acceptance script on a real Windows host and retain the build/test logs.
- [ ] Run the existing SQLite repository tests on Windows.
- [ ] Run PostgreSQL tests against a disposable local PostgreSQL instance.
- [ ] Apply migrations to a disposable database and verify schema/index definitions.
- [ ] Run one end-to-end approved prospecting Run and inspect Candidate/Evidence/Score persistence.
- [ ] Verify email sending and website publishing remain manual-approval-only.

## Activation rule

Until Workstreams A–D are implemented and tested, retain the single-active-application-instance restriction. A new `TryClaimLeaseAsync` primitive now performs the database-atomic claim, but the HostedService still calls legacy `ClaimQueuedAsync`. The new primitive is not active until ADO transaction enlistment, PostgreSQL concurrency tests, heartbeat, child-write fencing, recovery, and terminal transitions are complete. The current `RecoverInterruptedAsync` legacy path is only for lease-less rows and must not be used as multi-instance lease recovery. `RecoverExpiredLeasesAsync` handles expired Running and CancelRequested leases but remains unwired until the complete lease lifecycle is integrated.

A source-level contract test also guards the expired-lease recovery predicate and verifies that the HostedService has not yet activated the primitive. These assertions only guard SQL/transaction shape; they are not provider-backed tests. A green SQLite test suite, a source-level assertion, or a successful build alone does not authorize multi-instance deployment.


### Windows acceptance command

From a clean checkout of `kiger-dev`, run in PowerShell:

```powershell
.\backend\scripts\acceptance\Invoke-ProspectingWorkerAcceptance.ps1
```

Use `-SkipTests` only when performing a build-only pass. The script deliberately refuses another branch or a dirty working tree. It does not provision PostgreSQL and does not authorize multi-instance operation.


The child-write guard was tightened after review: lease expiry is now evaluated by an outer query after a materialized `FOR UPDATE` CTE has acquired the Run row. This avoids relying on a clock predicate evaluated before a potentially blocking row-lock wait. A source-level assertion checks the materialized CTE and post-lock expiry predicate; a real two-connection PostgreSQL test is still required.


## Legacy transition isolation (2026-11-04)

The legacy `ClaimQueuedAsync`, `RecoverInterruptedAsync`, `CompleteAsync`, and `FailAsync` paths now require both `LeaseToken IS NULL` and `LeaseExpiresAt IS NULL` in their conditional updates. This prevents legacy worker code from claiming, recovering, completing, or failing a row once lease metadata is present. Legacy lease-less Runs remain supported while the HostedService migration is incomplete. This is a compatibility guard, not activation of the lease protocol; the HostedService still uses legacy methods and remains single-active-instance only. A source-level contract test checks the legacy predicates; runtime verification remains pending.


## Reject partial lease metadata in legacy paths (2026-11-04)

The legacy claim, recovery, completion, and failure predicates now require all four lease fields to be NULL: `lease_owner`, `lease_token`, `lease_heartbeat_at`, and `lease_expires_at`. Checking only token and expiry could allow a malformed/partially migrated row carrying owner or heartbeat metadata to be mutated by the legacy worker. The source contract test now asserts all four fields across the legacy transitions. This remains a compatibility hardening step; HostedService lease claim/heartbeat/write fencing is not yet integrated.


## Expired cancellation lease recovery (2026-11-04)

Expired-lease recovery now handles both active states without losing cancellation intent: an expired `Running` lease is returned to `Queued`, while an expired `CancelRequested` lease is finalized as `Cancelled` with the database clock. Both paths require a non-null expired lease and tenant scope, and both clear active lease metadata. Lease-less legacy rows remain outside this recovery operation. This closes the abandoned-cancellation case where the prior fenced cancel operation correctly refused an expired lease but recovery previously ignored `CancelRequested` rows.


## Worker tenant identity must fail closed (2026-11-04)

The queue consumer rejects messages with non-positive Run IDs or Tenant IDs before creating a tenant scope or touching repositories. Startup recovery now throws when the tenant-scope runner does not provide an explicit tenant ID; it must never substitute tenant 0. This is a tenant-isolation guard, not a lease activation step. The source contract test asserts both boundaries.


## Worker activation tripwire (2026-11-04)

A source-level activation tripwire now asserts that the HostedService remains single-active (UpdateMaxConcurrentTasks(1)) and continues using only the legacy lease-less claim/recovery/terminal path until the complete lifecycle is integrated. It explicitly rejects a partial switch to atomic claim without child-write fencing, heartbeat, and fenced terminal transitions. This is a temporary safety test and must be deliberately replaced—not simply deleted—when the complete Worker integration lands.

### Existing Worker transaction inventory

The current processing flow has two distinct child-persistence entry points:

1. New Candidate path: requiresNew: true transactional UnitOfWork inserts Candidate, then Evidence, then the initial Score snapshot, and commits all three together. Candidate identity collision is handled after the failed UnitOfWork is disposed, followed by a tenant-scoped winner lookup and provenance repair.
2. Existing Candidate / collision repair path: RepairCandidateProvenanceAsync reads existing Evidence and Score, then opens a separate requiresNew: true transaction to add missing Evidence and (when absent) the initial Score. It deliberately does not modify review, approval, or conversion state.

Both transaction entry points must call TryLockLeaseForWriteAsync as the first database operation inside their own transaction, using the exact lease token and fencing version returned by the claim. The lock must remain held until the child transaction commits. A pre-transaction Run read or a guard executed in a different scope/transaction does not fence the child writes.

### Required lease-aware Worker sequence

1. Validate queue envelope and enter the message tenant scope.
2. Atomically claim with a stable worker identity; retain (RunId, TenantId, LeaseToken, FencingVersion) as immutable in-memory lease identity.
3. Start a linked per-run cancellation source and a heartbeat loop in an independent DI scope. Renew before the safety margin; a false renewal or heartbeat exception cancels the run token and permanently marks this execution as lease-lost.
4. Pass the linked token to provider calls and every database operation. After lease-lost, do not attempt legacy Fail/Complete or any further persistence.
5. For each new Candidate transaction and each provenance-repair transaction, call TryLockLeaseForWriteAsync before child reads/writes that depend on ownership; hold the row lock through commit.
6. On normal completion call the fenced Complete method. If persisted state is CancelRequested, call the fenced Cancel method instead. Do not interpret a false terminal result as success.
7. On provider/processing failure, call fenced Fail only if the lease is still live and cancellation has not taken precedence. If status is CancelRequested, use fenced Cancel. If ownership was lost, do not write a terminal state; let expiry recovery decide.
8. Stop and await heartbeat before disposing the tenant scope and worker scope.
9. Startup recovery must use expired-lease recovery for lease-backed rows and must not blanket-requeue lease-less rows once lease mode is activated. Keep the single-active restriction until PostgreSQL two-connection tests cover takeover racing with child writes and terminal transitions.