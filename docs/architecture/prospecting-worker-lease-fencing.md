# Prospecting Worker Lease and Fencing Design

## Status

Design gate for the prospecting worker. Until the implementation and PostgreSQL integration tests in this document are complete, the worker is **single-active-instance only**. Do not enable multi-instance deployment or run startup recovery concurrently with live workers.

## Why the current claim is insufficient

`ClaimQueuedAsync` conditionally changes Queued to Running, which prevents two workers from claiming the same queued row at the same instant. It does not fence a worker after process pause, network partition, or lease expiry. Startup recovery currently changes every Running row back to Queued. An old worker could resume after a replacement has claimed the run and still write Candidate, Evidence, or Score rows.

A status check before a write is not sufficient: the lease may change between the check and the write.

## Required persisted fields

Add to `Crm_Prospecting_Run` and the mapped entity:

- `Lease_Owner` varchar(160), nullable: stable worker-instance identifier.
- `Lease_Token` uuid, nullable: a new random token for every successful claim/reclaim.
- `Lease_Expires_At` timestamptz, nullable.
- `Lease_Heartbeat_At` timestamptz, nullable.
- `Fencing_Version` bigint NOT NULL DEFAULT 0: monotonically incremented on each successful claim.
- Index `(tenant_id, status, lease_expires_at)` for recovery/expiry scans.

Keep schema changes additive and idempotent. Existing Running rows must not be silently assigned a valid lease; migration should leave lease fields null so controlled recovery can requeue them before enabling the new worker.

## Atomic claim protocol

Use one database conditional UPDATE (or PostgreSQL UPDATE ... RETURNING) in a transaction:

1. Match tenant + Run ID and either Queued, or Running with an expired lease.
2. Set Running, owner, new UUID token, heartbeat time, expiry time.
3. Increment Fencing_Version.
4. Return the complete claimed row including token and version.

A worker may execute only when it has the returned token/version. A read after an update is not an acceptable substitute for RETURNING because it can observe a later worker's claim.

## Heartbeat protocol

Heartbeat updates must match tenant + Run ID + Lease_Token + Fencing_Version + Running status. Extend expiry only when the current token/version still matches. A zero-row update means ownership was lost: cancel provider work and stop all persistence immediately.

Heartbeat interval should be no more than one third of the lease duration. Lease duration must exceed expected transient database latency and be configurable with validated lower/upper bounds.

## Fencing every write

A lease validation query followed by an unrelated write is racy. Every Candidate/Evidence/Score persistence transaction and terminal Run transition must be fenced in the same database transaction:

1. Lock the Run row with SELECT ... FOR UPDATE (or use a single SQL statement whose predicate includes token and version).
2. Verify status, token, version, and non-expired lease.
3. Perform all related writes in that transaction.
4. Commit.

Terminal Complete/Fail/Cancel updates must include token + version. A stale worker must never be able to overwrite a newer worker's state or append evidence/scores.

## Recovery

Remove blanket Running -> Queued recovery once lease support is active. Requeue only rows whose lease is expired, using a conditional update and incrementing the fencing version on the next claim. Preserve CancelRequested precedence. Never recover a non-expired lease merely because a process started.

## Queue semantics

Redis messages remain hints, not ownership. Duplicate messages are harmless because the database claim is authoritative. Expired leases must be discoverable from the database even if the Redis message was lost; recovery should enqueue eligible IDs after the database transition.

## Required tests before enabling multiple instances

PostgreSQL integration tests using two independent connections and separate scopes:

- Two simultaneous claims of one Queued Run: exactly one succeeds.
- Worker A claims; lease expires; worker B reclaims: fencing version increases and token changes.
- Worker A attempts heartbeat after B reclaims: zero rows updated.
- Worker A attempts Candidate/Evidence/Score transaction after B reclaims: transaction rejected and no rows written.
- Worker A attempts Complete/Fail after B reclaims: zero rows updated.
- Duplicate Redis messages: only current database lease owner proceeds.
- Process restart with a live, non-expired lease: no premature requeue.
- Expired lease and CancelRequested: cancellation is not converted back to executable work.
- Tenant isolation: a token from one tenant cannot renew or write another tenant's Run.

## Rollout gate

1. Apply additive migration.
2. Deploy code with lease enforcement but keep deployment to one worker instance.
3. Run the PostgreSQL concurrency suite.
4. Verify heartbeat and stale-owner rejection in logs/metrics.
5. Only then permit multiple worker instances and remove the single-instance operational restriction.

No multi-instance claim should be made until all tests above pass against PostgreSQL.

## Schema and worker lifecycle integration landed (2026-11-04)

Migration `5.9.30` and the Run entity now contain additive lease metadata:

- `lease_owner varchar(160) NULL`
- `lease_token uuid NULL`
- `lease_expires_at timestamptz NULL`
- `lease_heartbeat_at timestamptz NULL`
- `fencing_version bigint NOT NULL DEFAULT 0`
- index `ix_crm_prospecting_run_te_st_leaseexp` on `(tenant_id, status, lease_expires_at)`

The migration is guarded by `to_regclass('crm_prospecting_run')`, uses `ADD COLUMN IF NOT EXISTS`, and leaves existing lease ownership NULL. The HostedService now uses `TryClaimLeaseAsync`, opportunistic database-clock renewal between provider calls and every 25 candidate records, `TryLockLeaseForWriteAsync` inside Candidate/Evidence/Score transactions, and fenced Complete/Cancel/Fail transitions. Startup recovery reclaims only expired lease-backed rows before rebuilding the queue. The worker remains single-active-instance only until independent PostgreSQL concurrency tests pass. There is not yet an independent heartbeat task.

Source-level tests now check entity mapping, additive migration, worker lease claims, transactional child-write fences, renewal and terminal transitions. Neither source tests nor migration have been executed in this environment; PostgreSQL migration and two-connection tests remain required.

The Run repository's SQLite integration suite also contains a CodeFirst round-trip test for all five lease fields (owner, UUID token, expiry, heartbeat, and fencing version). This checks entity-to-provider mapping only; it does not prove PostgreSQL migration correctness, atomic claim behavior, or stale-worker fencing. The test has been added but not executed.

## Fenced repository primitives landed (2026-11-04)

The Run repository now exposes three conditional-update primitives:

- `RenewLeaseAsync`: matches tenant-scoped Run ID, Running status, exact token, exact fencing version, and an unexpired lease; validates a positive lease duration no greater than one hour.
- `CompleteFencedAsync`: matches the same ownership predicate, writes terminal result fields, and clears owner/token/expiry/heartbeat while retaining the fencing version for audit.
- `FailFencedAsync`: applies the same predicate for failure and clears active lease metadata.
- `CancelFencedAsync`: finalizes `CancelRequested -> Cancelled` only for the exact token/version while its lease remains valid; cancellation intent cannot be turned into success or failure by a stale worker.

SQLite integration test cases have been added for wrong token, wrong version, expired lease, tenant isolation, successful heartbeat, successful completion/failure/cancellation, cancellation intent preservation, and lease cleanup.

**Important boundary:** these methods are not yet called by the hosted worker. The existing `ClaimQueuedAsync`, blanket startup recovery, candidate/evidence/score transaction, and current Complete/Fail paths remain unchanged. In particular, no lease claim currently creates a token or increments the fencing version. These primitives therefore do not yet make worker execution safe for multiple instances. Tests are authored but have not been run.

A further SQLite repository test now checks that a tenant holding another tenant's Run ID and token cannot renew, complete, or fail that Run. The test verifies that the original tenant's status, token, and fencing version remain unchanged. This is a repository query-filter regression check, not a substitute for PostgreSQL row-locking or two-connection concurrency tests.

## Next implementation gate: transaction and SQL client access

The CRM module obtains its client through `ISqlSugarClientResolver` and inherits from the framework's `SqlSugarRepositoryBase` via `SaasRepository<TEntity>`. The base implementation is supplied by the XiHan.Framework NuGet dependency in the standalone BasicApp build, not present as source in this repository. Before introducing PostgreSQL-specific `UPDATE ... RETURNING` or `SELECT ... FOR UPDATE`, confirm the resolved SqlSugar client/ADO APIs and how its active transaction/UoW connection is shared across repositories. Do not introduce a second connection or raw SQL path that bypasses the strict tenant query filter.

The next safe implementation must make the Run ownership check and Candidate/Evidence/Score writes share one transaction and one connection. A separate lease check followed by normal repository writes remains vulnerable to a time-of-check/time-of-use race. Until that transaction boundary is implemented and exercised against PostgreSQL, the existing worker must remain single-active-instance and the new fenced methods remain unused primitives.
