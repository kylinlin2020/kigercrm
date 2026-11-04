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

Migration `5.9.21.sql` and the Evidence entity now use the same ordinary composite unique-index shape. The entity declares the CodeFirst index over tenant, candidate, provider, normalized provider-record ID, field name, URL fingerprint, and normalized payload hash. The migration adds `source_url_hash`, backfills it with uppercase PostgreSQL `pgcrypto` SHA-256 digest, converts nullable identity values to empty strings, and creates the matching unique index. The full source URL remains stored for reviewer display but is not part of the index key, avoiding oversized B-tree entries for long URLs.

The URL fingerprint is a SHA-256 index-size optimization. Migration 5.9.21 requires PostgreSQL `pgcrypto` (the migration requests extension creation; a database administrator may need to install it first). The payload hash remains SHA-256 when supplied by the provider. Before rollout:

1. Verify fresh-tenant CodeFirst creates `source_url_hash`, non-null identity columns, and the expected unique index.
2. Verify existing tenants run migration 5.9.21 and the resulting index definition matches the entity metadata.
3. Exercise maximum-length and multibyte URLs and representative maximum-length identity fields against the actual PostgreSQL version.
4. Verify historical duplicate preflight and retry behavior on a disposable PostgreSQL database.
5. Do not enable multi-worker execution based on this index; lease/fencing acceptance remains a separate prerequisite.

This is a source-level schema alignment, not evidence of a successful migration or PostgreSQL integration test.

## 2026-11-04 Candidate identity collision hardening

The Worker now keeps the Run/provider/external-record identity as a typed `(ProviderCode, ExternalId)` tuple in both its in-memory set and Candidate lookup dictionary. This matches ordinal, case-sensitive string equality without relying on a delimiter that provider-controlled values could contain. A source-contract test guards the tuple construction and rejects the old interpolated `ProviderCode::ExternalId` key. This is source-level coverage only; the test has not been executed.

## Multi-process lease implementation specification

The required PostgreSQL lease/fencing design is documented in [PROSPECTING-WORKER-LEASE-DESIGN.md](PROSPECTING-WORKER-LEASE-DESIGN.md). It defines the owner incarnation, expiry, heartbeat, monotonic generation, row-lock fencing for Candidate/Evidence/Score writes, and independent-connection acceptance tests. It is a design specification, not implemented functionality. Continue deploying exactly one active prospecting Worker until those changes and tests are complete.


## 2026-11-04 Evidence uniqueness conflict behavior and recovery gate

Evidence writes are protected by a tenant-scoped composite unique index. The Worker has two transactional write paths:

- New Candidate: Candidate, its Evidence rows, and the first Score snapshot are inserted in one explicit transaction.
- Existing Candidate repair: missing Evidence rows and a missing first Score snapshot are inserted in one explicit transaction.

A unique-key collision in either path must be treated as a transaction failure. Do not catch the database exception inside the failed UnitOfWork and continue querying or writing through that same transaction/connection; its state may already be aborted. Do not treat every database exception as an idempotent duplicate.

**Current limitation:** the existing-Candidate repair path does not yet open a fresh UnitOfWork, reload the Evidence identity tuple, and prove that every conflicting row was committed by another writer. The exception currently reaches the Worker's outer failure handler, which marks the Run Failed. Startup recovery only requeues Running Runs, not Failed Runs. Therefore an Evidence unique-key collision is not yet an automatically recovered retry.

Required implementation before claiming automatic collision recovery:

1. Dispose/rollback the failed UnitOfWork completely.
2. Classify the conflict narrowly as the Evidence identity unique constraint (not a generic SQL or SqlSugar exception).
3. Open a fresh UnitOfWork and reload the full identity tuple: provider code, provider record ID, field name, source URL hash, and raw payload hash, scoped by the already tenant-scoped Candidate.
4. Treat the collision as idempotent only if every expected identity is now present and matches exactly.
5. Re-evaluate whether the Score snapshot still needs repair; a rolled-back transaction may have rolled back both Evidence and Score.
6. Retry only the still-missing rows in a new transaction, with a small bounded retry count. Propagate unrelated database errors.
7. Add SQLite integration coverage for rollback/reload/idempotent success and PostgreSQL two-connection tests for the actual unique-constraint race.

Until this is implemented, do not manually flip a Failed Run back to Queued in production. Preserve the failed Run and logs for diagnosis; rerun only through a supported application workflow after the collision has been resolved. This gate is separate from the single-worker deployment rule.

## 2026-11-04 Lead conversion concurrent retry boundary

Lead conversion uses a transactional application UnitOfWork and writes the source Lead ID onto Customer under a tenant-scoped unique index. If a competing conversion wins and commits, a subsequent request reloads the Lead, verifies its existing Customer/Contact references, and returns the converted Lead without creating new records. A source-contract test now guards this idempotent retry branch.

The losing request in a simultaneous race is **not yet guaranteed to transparently return the winner's result in the same request**: the unique-index exception may surface to that caller. The database constraint prevents duplicate Customers; a client retry after the winner commits should take the already-converted branch. A PostgreSQL two-connection service-level test and narrowly classified conflict recovery are still required before describing this as seamless concurrent conversion.

## 2026-11-04 Provider plan validation boundary

Strategy creation now validates the provider plan before allocating an immutable strategy version. The plan must be a non-empty JSON array containing at most 32 distinct provider codes; each entry must be a non-blank string of at most 80 characters, without control characters. Duplicate codes are rejected case-insensitively because provider registration and dispatch use case-insensitive provider-code lookup.

Run creation repeats the validation against the persisted active strategy before taking the execution snapshot. This protects the execution boundary from malformed legacy rows as well as newly submitted strategy input. A syntactically valid code that has no registered provider is still handled as a per-provider Run warning/failure by the Worker; registration availability is deployment configuration and is not inferred during strategy authoring.

The validation is covered by source-level application wiring checks and unit test sources for empty, blank, duplicate, non-string, oversized, over-count, and control-character entries. These tests have not yet been executed in CI or on the Windows acceptance environment.

## Evidence collision rollback preflight

The SQLite evidence identity integration suite now includes a transaction-boundary preflight: it inserts one evidence row, deliberately attempts the same unique identity in the same transaction, rolls the transaction back, and only then reads the candidate's evidence. The expected persisted set is empty. This documents the critical rule that reconciliation must not query or write inside a transaction after a uniqueness violation.

This preflight does not implement automatic collision recovery and does not prove PostgreSQL's aborted-transaction behavior. Production recovery still requires the worker to dispose the failed UnitOfWork, open a fresh UnitOfWork, compare the complete tenant/candidate/provider-record/field/source-hash/payload-hash identity tuple, and verify or repair the score snapshot before treating the retry as idempotent.
