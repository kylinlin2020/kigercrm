# Prospecting Worker: SqlSugar Atomic Access Audit

**Audit date:** 2026-11-04  
**Target branch:** `kiger-dev`  
**Scope:** Resolve the repository/framework uncertainty blocking PostgreSQL lease claim and same-transaction fencing.

## Findings

The CRM repository does not need to open a second database connection to reach the active tenant-scoped SqlSugar client.

The current framework implementation is publicly available at the pinned source revision:

- [SqlSugarRepositoryBase.cs](https://github.com/XiHanFun/XiHan.Framework/blob/c112307162da233c09541a186dcd5fc0c03fd1d7/framework/src/XiHan.Framework.Data/SqlSugar/Repository/SqlSugarRepositoryBase.cs)
- [SqlSugarReadOnlyRepository.cs](https://github.com/XiHanFun/XiHan.Framework/blob/c112307162da233c09541a186dcd5fc0c03fd1d7/framework/src/XiHan.Framework.Data/SqlSugar/Repository/SqlSugarReadOnlyRepository.cs)
- [Data package transaction and resolver documentation](https://github.com/XiHanFun/XiHan.Framework/blob/c112307162da233c09541a186dcd5fc0c03fd1d7/docs/packages/data.md)

The source confirms:

1. `SqlSugarReadOnlyRepository<TEntity,TKey>` exposes a protected `DbClient` property.
2. `DbClient` resolves through `ISqlSugarClientResolver.GetClientForEntity(typeof(TEntity))`, so the Run repository can use the same entity-specific, current-tenant client already used by inherited repository operations.
3. `CreateQueryable()` retains the framework's global tenant and soft-delete filters.
4. The framework's data package documents that the resolver registers the client in the transaction-aware UnitOfWork, and the SqlSugar transaction adapter wraps the client's ADO transaction.
5. Conditional repository `UpdateAsync(columns, predicate)` adds a strict current-tenant write predicate. It is not, however, a substitute for PostgreSQL `UPDATE ... RETURNING`: it returns only whether any row changed, not the exact lease token/version granted by that statement.

This resolves the earlier uncertainty about whether a repository subclass can access its active client. The Run repository now contains an additive `TryClaimLeaseAsync` implementation using inherited `DbClient`, PostgreSQL `UPDATE ... RETURNING`, explicit tenant predicate, database `clock_timestamp()`, and an atomic fencing-version increment. A source-level contract test guards these properties. This does **not** yet prove that the raw SQL is correctly mapped or transactionally enlisted in this application; provider-backed tests and a real build remain required.

## Safe implementation direction

The implemented claim primitive remains inside `CrmProspectingRunRepository` and uses its inherited `DbClient`, rather than constructing an independent Npgsql connection.

The current PostgreSQL claim uses one statement with a materialized database-clock CTE and is structurally equivalent to:

```sql
UPDATE crm_prospecting_run
SET status = @running,
    lease_owner = @owner,
    lease_token = @new_token,
    lease_heartbeat_at = @now,
    lease_expires_at = @expires_at,
    fencing_version = fencing_version + 1,
    started_at = COALESCE(started_at, @now)
WHERE tenant_id = @tenant_id
  AND basic_id = @run_id
  AND (
      status = @queued
      OR (status = @running AND lease_expires_at <= @now)
  )
RETURNING *;
```

The current implementation uses the physical table/column names from the versioned migration and entity metadata. Parameter binding and row materialization still require compilation and a real PostgreSQL test against the resolved SqlSugarCore 5.1.4.221 package. The explicit `tenant_id` predicate is required even though the active client is tenant-aware: it makes the raw statement's isolation boundary reviewable and prevents an accidental cross-tenant update if query filters are not applied to ADO SQL.

The returned row is materialized directly from this statement. Do not perform a follow-up SELECT to discover the token/version.

The lease duration and owner identifier must be validated before executing the statement. A fresh UUID must be generated for every claim attempt. The database must increment `fencing_version` as part of the same UPDATE.

## Same-transaction fencing requirements

A future Candidate/Evidence/Score write unit must:

- run under an explicit transactional UnitOfWork;
- use the same tenant scope and entity-resolved SqlSugar client/connection;
- lock and verify the Run row (or use an equivalent conditional SQL write guard) before child inserts;
- keep the lease verification and all related writes in the same transaction;
- reject a missing, expired, replaced, or mismatched token/version;
- never treat a preflight read outside the write transaction as authorization.

The worker must stop persistence immediately when heartbeat renewal affects zero rows. Provider calls may need cancellation propagation, but cancellation of a provider call alone is not the fencing boundary.

## Recovery and compatibility gate

Do not change blanket startup recovery until the new claim path and a migration/recovery policy for legacy Running rows are implemented together. Existing rows created before lease activation have NULL lease metadata; they must not be interpreted as live leases.

Do not enable multiple worker instances. The existing hosted worker still calls the legacy `ClaimQueuedAsync`, and its Candidate/Evidence/Score writes are not lease-fenced.

## Required verification before activation

- SqlSugar PostgreSQL test proving the ADO `UPDATE ... RETURNING` command runs on the active UnitOfWork connection.
- Two independent scopes/connections racing to claim one queued Run: exactly one gets a returned row.
- Expired lease takeover increments version and changes token.
- Old token cannot renew, complete, fail, cancel, or persist Candidate/Evidence/Score after takeover.
- Tenant A token cannot claim or mutate Tenant B rows.
- Rollback test proving a failed child-write transaction leaves no partial Candidate/Evidence/Score data.
- Windows-native restore, build, test, migration, and runtime acceptance.

## Current status

The atomic claim method and a source-level contract test are committed to `kiger-dev`; the HostedService has not been switched to it. No .NET build, test, PostgreSQL migration, two-connection concurrency test, or Windows runtime test was executed. The single-active-worker restriction remains in force.
