# Prospecting Worker database lease and fencing design

Date: 2026-11-04

Status: partially implemented. Schema and an additive PostgreSQL atomic-claim primitive exist; lifecycle integration, child-write fencing, PostgreSQL verification, and multi-instance approval remain outstanding.

## Current safety boundary

The current Run repository uses a conditional Queued -> Running update, but startup recovery resets every Running row in the tenant to Queued. Complete/Fail identify a Run by ID and status, not by process ownership. This is safe only with exactly one active prospecting Worker and no overlapping startup recovery.

Redis is a delivery mechanism only. PostgreSQL must be authoritative for ownership and stale-worker rejection.

## Run lease fields already added

The additive 5.9.30 migration and entity metadata currently define:

- `lease_owner varchar(160) NULL`: process-incarnation identity.
- `lease_token uuid NULL`: fresh ownership token per claim attempt.
- `lease_expires_at timestamptz NULL`: database-time lease deadline.
- `lease_heartbeat_at timestamptz NULL`: last successful heartbeat.
- `fencing_version bigint NOT NULL DEFAULT 0`: monotonically increasing fencing generation.

The atomic claim increments `fencing_version` in the database and returns the exact row containing the new token/version. The fields are additive groundwork; the worker does not yet use them.

## State transitions

1. Claim: the repository now has an additive PostgreSQL conditional UPDATE ... RETURNING primitive for a Queued Run or a Running Run with a non-null expired lease. It sets owner/token, increments fencing_version, and writes heartbeat/expiry using one database-clock CTE. It is not yet wired into the worker or verified against a live PostgreSQL database.
2. Heartbeat: update heartbeat and expiry only when tenant, Run ID, lease token, fencing_version, Running status, and an unexpired lease all match. A zero-row update means ownership is lost; stop processing immediately.
3. Complete/Fail: terminal transition must include tenant, Run ID, lease token, fencing_version and expected status in the WHERE predicate. A stale generation must update zero rows.
4. Cancellation: cancellation changes Running to CancelRequested without clearing lease identity. The current owner may finalize Cancelled only when owner and generation still match. An expired owner cannot finalize.
5. Recovery: remove blanket Running -> Queued startup recovery. Workers periodically discover queued Runs and expired leases; expired takeover uses the same atomic claim operation. CancelRequested is finalized only by a valid owner or an explicit safe expiry-recovery transition.
6. Queue: Redis messages carry tenant + Run ID and remain hints. Duplicate, delayed, or lost messages cannot confer ownership.

All lease times must be generated/read using PostgreSQL time (for example `clock_timestamp()`), not worker wall clocks. Heartbeat interval must be materially shorter than lease TTL; choose values only after measuring provider call durations and shutdown behavior.

## Fencing every durable write

A lease check performed before a Candidate insert is insufficient. A worker can lose its lease between the check and the write.

Every Candidate + Evidence persistence transaction must:
1. Begin a PostgreSQL transaction.
2. Lock the Run row for the same tenant and Run ID (`SELECT ... FOR UPDATE`).
3. Re-check lease token, fencing_version, Running status, and `lease_expires_at > clock_timestamp()` while holding the row lock.
4. Insert Candidate and Evidence rows in that same transaction.
5. Commit.

Takeover must update/lock that same Run row. It therefore waits for an in-flight valid transaction; after takeover commits, an old generation cannot begin another candidate transaction. Apply the same fencing check to any future Score writes, raw-result checkpoint, progress counter, and terminal transition. Never rely only on an in-memory cancellation token.

Do not hold the Run row lock during provider network calls, serialization, or lead duplicate lookup. Those operations happen outside the candidate transaction; ownership must be checked again before every durable mutation.

## Migration and compatibility rollout

- Add fields through a new versioned PostgreSQL migration for existing tenant databases and matching entity metadata for fresh CodeFirst tenants.
- Verify fresh-tenant CodeFirst schema and upgrade-path schema have identical column types, nullability, defaults, and indexes.
- Deploy a compatibility release that still enforces single Worker while lease fields are observed and validated.
- Enable lease claim/heartbeat/fencing only after independent PostgreSQL tests pass.
- Remove legacy blanket recovery only in the same release that activates lease-aware recovery; never run both algorithms.
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

- This design does not authorize multiple workers until all lifecycle and child-write fencing is integrated and tested. The claim primitive alone is insufficient.
- Redis locks are not a replacement for the PostgreSQL generation token.
- A lease does not make provider calls exactly-once. A crash after a provider accepted a request but before the result was checkpointed can cause a repeat call; adapters must use provider idempotency where available and expose retry/cost metadata.
- Decide lease TTL, heartbeat cadence, polling strategy, and maximum concurrent Runs from measured workload and graceful-shutdown constraints, not arbitrary defaults.
