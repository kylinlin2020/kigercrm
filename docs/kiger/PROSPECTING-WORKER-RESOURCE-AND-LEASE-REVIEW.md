# Prospecting Worker Resource and Lease Review

## Source-review findings

This document records implementation constraints found in the current worker and repository source. It is not a runtime test report.

### 1. Aggregate output check happens too late to bound allocation

The worker caps the number of retained raw records at 2,000 and checks the UTF-8 size of the fully serialized aggregate against 5,000,000 bytes. However, the serialization and byte-count check happen only after all provider results have been retained in memory.

A single provider-controlled record can contain extremely large strings. The worker can therefore allocate a large object graph and serialized string before the aggregate size guard rejects the Run. The per-field Candidate truncation does not protect the raw Run snapshot because that snapshot is serialized before Candidate normalization.

Required change:

- Define explicit maximum lengths for every raw provider field and reject or safely truncate records before adding them to the retained raw list.
- Prefer reject-and-count over silently truncating source facts used for evidence; if truncation is permitted for display-only fields, retain a separate bounded raw-evidence representation and make the truncation explicit.
- Enforce a conservative per-record serialized UTF-8 budget and a cumulative budget while collecting records, not only after aggregate serialization.
- Avoid serializing a provider-controlled object more than once where possible.
- Count and record bounded warnings for rejected oversized records.
- Ensure the aggregate budget check uses overflow-safe accounting and includes JSON framing/escaping overhead.

### 2. Fallback identity serialization repeats the unbounded-input risk

When ExternalId is blank, the worker serializes the complete provider record to calculate its identity. This is performed during normalization, after the aggregate serialization. A large record can therefore trigger additional large allocations.

Required change:

- Derive identity from a deterministic, bounded canonical representation.
- Bound and validate the input before serialization.
- Reuse a single canonical serialization and digest where possible.
- Test deterministic behavior across restarts and serializer configuration changes.

### 3. Multi-instance recovery is unsafe without leases

The current Run repository conditionally claims Queued -> Running, which prevents two workers from claiming the same queued row at the same instant. However, startup recovery changes every Running row in the tenant to Queued without proving that its owner is dead or its lease expired.

A second worker starting while the first is still processing can requeue the live Run, after which another worker may claim it. Conditional claim alone does not prevent this startup race or fence stale writes.

Required operational rule:

- Run exactly one active prospecting worker instance until lease ownership, heartbeat, expiry, and fencing tokens are implemented.
- Do not interpret the current conditional claim as multi-instance safety.

Required implementation before horizontal scaling:

- Atomically claim Queued or expired Running rows using database time.
- Store owner ID, expiry, heartbeat, and monotonically increasing lease generation.
- Fence every heartbeat and terminal state transition by owner and generation.
- Fence Candidate, Evidence, and Score writes in the same database transaction as a Run-row ownership/lease validation.
- Remove blanket startup recovery of all Running rows.
- Test competing workers and stale-owner writes using independent PostgreSQL connections and processes.

### 4. Required test matrix

Resource bounds:
- Single oversized field.
- Many near-limit records.
- Escaped Unicode and control characters.
- A provider result exceeding the per-record budget.
- Aggregate budget reached across multiple providers.
- Cancellation while records are being validated.
- Ensure no oversized raw snapshot is committed.

Lease and concurrency:
- Two workers competing for one queued Run.
- Live worker plus second worker startup.
- Expired lease takeover.
- Old owner heartbeat after takeover.
- Old owner Candidate/Evidence/Score write after takeover.
- Old owner Complete/Fail after takeover.
- Cancellation racing with lease expiry and completion.
- Tenant isolation.
- Database clock behavior and process clock skew.

## Current deployment boundary

Until these changes are implemented and verified with PostgreSQL integration tests, keep the prospecting worker single-instance. This review does not certify build, unit tests, migrations, or production readiness.
