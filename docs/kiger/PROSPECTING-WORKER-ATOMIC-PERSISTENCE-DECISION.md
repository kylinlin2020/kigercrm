# Prospecting Worker Atomic Persistence Decision

Status: implementation gate / architecture decision record
Branch: `kiger-dev`
Scope: Candidate + Evidence + Score persistence for automatic prospecting
Last audited: 2026-11-04

## Decision summary

Do not add Evidence or Score writes directly to `CrmProspectingRunHostedService` until a transaction boundary shared by all three repositories has been proven against the exact XiHan.Framework 4.8.0 runtime.

The Worker currently calls `ICrmProspectingCandidateRepository.AddAsync` once per accepted raw record. It does not call `CrmProspectingEvidenceFactory`, `CrmProspectingScoreSnapshotFactory`, or either immutable-row repository. The current reviewer endpoint can therefore legitimately return empty Evidence and Score collections.

The intended persistence operation is one short transaction per accepted raw envelope:

1. Normalize the Candidate and build Evidence rows from the original envelope before opening the transaction.
2. Start a short database unit of work.
3. Insert Candidate and obtain its generated `BasicId`.
4. Assign that ID to each Evidence row and insert those rows.
5. Insert a Score snapshot only when every scored fact is supported by explicit, validated source facts. Unknown facts remain unknown; do not manufacture score inputs.
6. Commit.
7. Only after commit, update in-memory duplicate sets and persisted counters.

Provider HTTP calls and the complete Run normalization loop must remain outside a database transaction. Never hold one transaction across the 2,000-record Run limit.

## Transaction API proof gate

The repository currently references XiHan.Framework packages at version 4.8.0. Before implementing the transaction coordinator, prove all of the following using the actual framework assemblies/runtime:

- The exact `IUnitOfWorkManager` namespace and supported `Begin` overload.
- The exact completion and disposal contract.
- All Candidate, Evidence, and Score repositories resolve the same tenant-aware SqlSugar client and enlist in the same active transaction.
- `AddAsync` assigns the generated Candidate `BasicId` before dependent Evidence insertion.
- An exception during Evidence or Score insertion rolls back all earlier inserts.
- A nested or ambient unit of work is not accidentally started independently by any repository.

Preferred architecture is a small application-service transaction coordinator invoked by the Worker, provided its framework interception/UnitOfWork behavior is verified. Do not assume an attribute on a hosted-service method is intercepted. Do not invent a UOW overload or assume that three scoped repositories automatically share a transaction merely because they share a DI scope.

A source-only check is not transaction proof. Required proof is a PostgreSQL integration test using independent database connections and the actual repository/UOW registrations.

## Idempotency and uniqueness findings

The Candidate entity declares a unique index over:

`TenantId, RunId, ProviderCode, ExternalId, IsDeleted`.

This is a useful database backstop for retrying the same provider identity within one Run. The Worker also maintains an in-memory key set populated from candidates already stored for that Run.

Important limits:

- The uniqueness scope is Run-scoped, not global across Runs. The same provider record can appear in separate Runs; cross-Run duplicate review remains a separate business rule.
- In-memory sets are an optimization only. They are not a concurrency guarantee.
- Candidate uniqueness does not automatically make Evidence/Score insertion idempotent.
- A retry after a committed Candidate but before separately committed Evidence would leave partial data. The required atomic transaction removes that partial-commit state.
- If a future design intentionally permits Candidate and Evidence to commit separately, it must introduce an explicit idempotent evidence key and reconciliation state; do not silently rely on the Candidate key.

The current Worker claims a queued Run before processing, but its process-local concurrency setting is not a distributed lease. Do not enable multi-instance Worker processing based only on that setting.

## Evidence and score policy

- Evidence must be built from the original `CrmProspectingRawEnvelope`, not reconstructed from the normalized Candidate.
- Evidence remains `unverified`; ingestion is not verification.
- Unsafe or missing source URLs produce no Evidence rows. Such records must not receive evidence-quality points.
- Do not hash low-entropy contact names or email addresses without a suitable keyed scheme.
- A Score snapshot is optional when facts are incomplete. Do not create a misleading zero-score or infer procurement intent from a generic summary.
- A high score or priority never approves a Candidate, converts a Lead, sends outreach, or publishes website content.
- Candidate remains PendingReview or Duplicate according to existing duplicate logic.

## PostgreSQL acceptance matrix

The integration suite must run against PostgreSQL and use independent connections (and, for race tests, independent scopes/processes):

| Scenario | Required assertion |
|---|---|
| Normal write | Candidate + all eligible Evidence + optional Score become visible after commit |
| Evidence insert failure | Candidate and partial Evidence are rolled back |
| Score insert failure | Candidate and Evidence are rolled back |
| Cancellation before commit | No partial record set remains |
| Same Run retry | Existing provider identity does not create a second Candidate or duplicate evidence set |
| Concurrent same-identity insert | Exactly one Candidate wins; loser is handled as an idempotent duplicate, not a silent Run-wide failure |
| Separate Runs | Same provider identity may exist in both Runs unless a deliberate cross-Run rule is added |
| Tenant isolation | Tenant A cannot read, attach, or mutate Tenant B Evidence/Score |
| Missing/unsafe source | No Evidence and no evidence-quality points |
| Candidate review read API | Evidence/Score returned only after campaign owner/data-scope authorization |

SQLite source tests may remain useful regression tests, but they do not prove PostgreSQL transaction, locking, or concurrency behavior.

## Worker lease and Run-state boundary

Keep one active Worker deployment until a database-authoritative lease is implemented. A production multi-instance lease requires owner identity, expiry, heartbeat, and a monotonically increasing fencing generation. Claim, heartbeat, candidate persistence, completion, and failure writes must reject stale generations. Process-local concurrency limits and startup recovery are not substitutes.

Redis/background queue messages are hints; the database must remain authoritative for Run ownership and state. Do not implement timestamp-only stale detection or a process-local mutex as a distributed lock.

## Separate validation gates

These remain distinct and must be reported separately:

1. Source inspection / source-level regression tests.
2. .NET restore and build.
3. Unit tests.
4. PostgreSQL migrations and integration tests.
5. Windows real pull, build, startup, and runtime acceptance.

As of this audit, this work has not been executed through .NET build/test or PostgreSQL integration tests. Windows acceptance remains open. PR #1 remains Draft and must not be merged before all required gates pass.
