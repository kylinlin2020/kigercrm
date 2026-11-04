# Prospecting Worker database lease and fencing design

Date: 2026-11-04

Status: independent heartbeat wired into the HostedService on 2026-11-04. Atomic claim, heartbeat and work-boundary renewal, transactional child-write fencing, fenced terminal transitions and expiry recovery are active in source. PostgreSQL verification and multi-instance approval remain outstanding.

## Current safety boundary

The HostedService now uses PostgreSQL lease claim, token/version renewal, transactional child-write fences, fenced terminal transitions and expired-lease recovery. It remains single-worker operationally until the PostgreSQL race suite passes. In addition to work-boundary renewal, an independent two-minute heartbeat uses a separate DI scope and repository instance. If renewal returns false or throws, it cancels the linked Run token; subsequent work stops and database fencing remains the final stale-writer guard.

Redis is a delivery mechanism only. PostgreSQL must be authoritative for ownership and stale-worker rejection.

## Run lease fields already added

The additive 5.9.30 migration and entity metadata define:

- `lease_owner varchar(160) NULL`: process-incarnation identity.
- `lease_token uuid NULL`: fresh ownership token per claim attempt.
- `lease_expires_at timestamptz NULL`: database-time lease deadline.
- `lease_heartbeat_at timestamptz NULL`: last successful heartbeat.
- `fencing_version bigint NOT NULL DEFAULT 0`: monotonically increasing fencing generation.

The atomic claim increments `fencing_version` in the database and returns the exact row containing the new token/version. The HostedService now uses the returned ownership identity.

## State transitions

1. Claim: the repository uses a PostgreSQL conditional UPDATE ... RETURNING for a Queued Run or a Running Run with a non-null expired lease. It sets owner/token, increments fencing_version, and writes heartbeat/expiry using one database-clock CTE. The HostedService consumes this claim protocol; it is not yet verified against live PostgreSQL.
2. Renewal: the worker renews with PostgreSQL time and exact tenant/Run/token/version predicates before and after provider requests, every 25 candidate records, and independently every two minutes. The heartbeat creates a fresh DI scope per tick so it does not share a DbContext/UoW with the processing path. A zero-row update or heartbeat exception cancels the linked Run token. Fenced child writes and terminal transitions remain mandatory even with heartbeat enabled.
3. Complete/Fail: the worker uses PostgreSQL-clock terminal methods whose predicates include tenant, Run ID, lease token, fencing_version, expected status and an unexpired lease. A stale generation updates zero rows.
4. Cancellation: cancellation changes Running to CancelRequested without clearing lease identity. The worker checks persisted cancellation before renewing or beginning another provider/normalization batch; the current owner finalizes Cancelled only with the exact live lease. If cancellation races the final Complete update, the worker attempts the fenced Cancel transition; expiry recovery also finalizes an expired CancelRequested Run.
5. Recovery: startup recovery calls RecoverExpiredLeasesAsync before enumerating queued IDs. Only expired lease-backed Running rows are requeued; expired CancelRequested rows become Cancelled. The legacy recovery method is restricted to lease-less rows. Redis queue reconstruction follows database recovery.
6. Queue: Redis messages carry tenant + Run ID and remain hints. Duplicate, delayed, or lost messages cannot confer ownership.

All lease times must be generated/read using PostgreSQL time (for example `clock_timestamp()`), not worker wall clocks. Heartbeat interval must be materially shorter than lease TTL; choose values only after measuring provider call durations and shutdown behavior.

## Fencing every durable write

A lease check performed before a Candidate insert is insufficient. A worker can lose its lease between the check and the write.

Every Candidate + Evidence + initial Score persistence transaction must:
1. Begin a PostgreSQL transaction.
2. Lock the Run row for the same tenant and Run ID (`SELECT ... FOR UPDATE`).
3. Re-check lease token, fencing_version, Running status, and `lease_expires_at > clock_timestamp()` while holding the row lock.
4. Insert Candidate, Evidence and initial Score rows in that same transaction.
5. Commit.

Takeover must update/lock that same Run row. It therefore waits for an in-flight valid transaction; after takeover commits, an old generation cannot begin another candidate transaction. Apply the same fencing check to any future Score writes, raw-result checkpoint, progress counter, and terminal transition. Never rely only on an in-memory cancellation token.

Do not hold the Run row lock during provider network calls, serialization, or lead duplicate lookup. Those operations happen outside the candidate transaction; ownership must be checked again before every durable mutation.

## Migration and compatibility rollout

- Add fields through a new versioned PostgreSQL migration for existing tenant databases and matching entity metadata for fresh CodeFirst tenants.
- Verify fresh-tenant CodeFirst schema and upgrade-path schema have identical column types, nullability, defaults, and indexes.
- Deploy the lease-aware worker while retaining the single-worker operational limit.
- Keep lease claim, work-boundary renewal, transactional child-write fencing and terminal fencing enabled; validate them with independent PostgreSQL connections before multi-instance rollout.
- Keep legacy recovery restricted to rows with all lease metadata NULL; do not broaden it to lease-backed rows.
- Keep the operational single-worker gate until production rollout and monitoring demonstrate lease expiry/takeover behavior.

## Required PostgreSQL acceptance tests

Use independent connections and preferably independent worker processes:

- Two processes claim one Queued Run: exactly one generation/owner succeeds.
- A second process cannot take over an unexpired Running lease.
- An expired lease is taken over exactly once; generation strictly increases.
- Old owner heartbeat, Candidate insert, Evidence insert, Score insert, Complete and Fail all affect zero rows or roll back after takeover.
- Candidate transaction holding the Run row lock and a concurrent takeover serialize correctly.
- Worker crash after Candidate/Evidence commit resumes without duplicate provider identity or missing evidence.
- Cancellation racing heartbeat, takeover and completion has one deterministic terminal result.
- Tenant A cannot claim, heartbeat, write, or complete tenant B's Run.
- Clock skew on worker hosts does not affect lease decisions.

## Non-goals and unresolved decisions

- This implementation does not authorize multiple workers until PostgreSQL integration and concurrency tests pass. Source-level wiring alone is insufficient.
- Redis locks are not a replacement for the PostgreSQL generation token.
- A lease does not make provider calls exactly-once. A crash after a provider accepted a request but before the result was checkpointed can cause a repeat call; adapters must use provider idempotency where available and expose retry/cost metadata.
- Decide lease TTL, heartbeat cadence, polling strategy, and maximum concurrent Runs from measured workload and graceful-shutdown constraints, not arbitrary defaults.
