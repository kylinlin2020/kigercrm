# Prospecting worker deployment and recovery runbook

## Current support boundary

**Run exactly one active application process with the prospecting hosted worker enabled.** The current implementation is not safe for multiple active worker instances.

This is a correctness constraint, not a throughput recommendation:

- `CrmProspectingRunHostedService.ExecuteAsync` invokes startup recovery before polling Redis.
- `RecoverInterruptedAsync` changes every tenant-scoped `Running` Run to `Queued`; it has no lease expiry, owner identity, heartbeat, or fencing token.
- A second application instance can therefore requeue a Run that the first instance is actively processing.
- The conditional `Queued -> Running` claim prevents two workers from claiming the same queued state, but it does not protect a live Run from another instance's startup recovery.
- Candidate persistence is currently performed row by row. The Run's terminal-state update does not fence candidate writes from an obsolete worker.

Do not infer multi-instance safety from Redis queue behavior, the conditional claim, or the SQLite repository tests.

## Deployment topology

Until database-authoritative leases and fencing are implemented and verified:

1. Deploy one application instance that runs `CrmProspectingRunHostedService`.
2. Do not horizontally scale that worker-bearing application deployment.
3. If web/API capacity must scale independently, ensure additional web/API instances do not start this hosted worker. The current repository must not be assumed to provide a worker-disable switch unless one is explicitly implemented and tested.
4. Do not run a second copy during rolling deployment. Stop the old worker-bearing process before starting the replacement.
5. Do not configure an orchestrator to briefly overlap old and new worker-bearing replicas during rollout.
6. Redis is a delivery queue, not the authoritative Run state store. The database status remains authoritative.

## Controlled restart procedure

A normal process restart is supported only under the single-active-worker constraint.

1. Stop the current worker-bearing application process and confirm it has exited.
2. Confirm no other application instance is running the prospecting hosted service.
3. Start exactly one replacement instance.
4. Allow startup recovery to run. Interrupted `Running` Runs are requeued; `CancelRequested` Runs are finalized as `Cancelled`.
5. Verify that queued work is being consumed and inspect failed or repeatedly requeued Runs before resubmitting business work.

Do not start a replacement while the previous process may still be alive (for example, during an uncertain shutdown or network partition). If process ownership is uncertain, keep all worker instances stopped until it is resolved.

## Operational checks

Before enabling the worker in a deployment:

- Verify only one worker-bearing process is active.
- Verify the database and Redis are reachable.
- Verify the configured provider set is intentional. The fake provider is opt-in and is not a production acquisition provider.
- Verify provider credentials and outbound network policy outside the repository.
- Confirm cancellation and failure states are visible to operators.
- Keep a backup and migration rollback plan for schema changes.

During operation, investigate:

- Runs remaining in `Running` after the worker process has stopped.
- Repeated provider failures or warnings.
- Runs repeatedly returning to `Queued`.
- Candidate counts that do not match the expected provider output.
- Any evidence of two active worker-bearing processes.

Persisted cancellation is checked between provider calls and periodically during candidate normalization; it cannot forcibly interrupt an arbitrary provider call that does not observe the cancellation token.

## Recovery and data consistency notes

- A duplicate Redis message is expected to be harmless when the Run is no longer `Queued`; `ClaimQueuedAsync` then returns no Run.
- Startup recovery is intentionally broad and is safe only after the previous worker process is confirmed stopped.
- Candidate records are persisted individually. If a process stops partway through normalization, retrying the same Run uses the existing provider/external-ID keys to avoid inserting those same candidate identities again. This is not a substitute for a transactional, fenced candidate-write protocol.
- Do not manually change Run statuses in production as a routine recovery mechanism. Preserve the Run snapshot and audit trail; use a controlled worker restart first.

## Gate before enabling multiple workers

Multi-instance operation must remain disabled until all of the following are implemented and verified:

1. Database-authoritative lease owner, expiry/heartbeat, and monotonically increasing fencing token.
2. Atomic claim and lease renewal with server-side time semantics and conditional predicates.
3. Every Run state transition and every candidate write validates the current owner and fencing token in the same database transaction as the write.
4. Startup recovery only reclaims expired leases; it never blindly requeues every `Running` Run.
5. Redis messages remain hints and cannot grant ownership.
6. PostgreSQL tests using independent connections/processes cover competing claims, live-lease exclusion, expired-lease takeover with a higher token, stale-owner rejection for renew/complete/fail/candidate writes, renewal-versus-expiry, cancellation-versus-reclaim, process-death recovery, and tenant isolation.
7. Deployment tests prove rolling updates and worker restarts cannot create overlapping owners.

SQLite tests are useful repository preflight checks but do not satisfy the PostgreSQL concurrency gate. Windows pull/build/runtime acceptance is a separate final gate.

## Evidence identity migration rollout gate (5.9.21)

Migration `5.9.21.sql` creates a PostgreSQL expression-based unique index. The current Evidence entity metadata does not declare an equivalent CodeFirst index, and fresh tenant provisioning may baseline the current migration version after creating tables from entity metadata rather than replaying every historical update script.

Therefore, do not assume that the unique index exists in a newly provisioned tenant merely because the Evidence table exists. Before production rollout:

1. Verify the actual fresh-tenant provisioning path and inspect `pg_indexes` for `ux_crm_prosp_evidence_identity`.
2. Verify upgrade-path tenants receive migration 5.9.21.
3. Add an explicit fresh-tenant schema path for this invariant (or revise the entity/schema strategy) before declaring the index universally present.
4. Verify the index definition and duplicate preflight on a disposable PostgreSQL database.
5. Do not enable multi-worker execution based on this index; lease/fencing acceptance remains a separate prerequisite.

The current index also includes a URL expression whose encoded index tuple size depends on real data and PostgreSQL page limits. Exercise maximum-length and multibyte URLs against the actual PostgreSQL version before rollout. This migration is committed source, not yet a verified production-ready schema change.
