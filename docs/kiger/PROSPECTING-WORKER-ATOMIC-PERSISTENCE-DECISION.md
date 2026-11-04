# Prospecting Worker Atomic Persistence Decision

Status: implementation and verification gate
Branch: `kiger-dev`
Scope: Candidate + Evidence + Score persistence for automatic prospecting
Last audited: 2026-11-04

## Current implementation

The Worker now persists eligible Candidate, Evidence, and the first Score snapshot together in an explicit transactional UnitOfWork for each newly accepted raw envelope. It also repairs missing Evidence and a missing first Score snapshot for an existing Candidate in a separate explicit transaction. Provider network calls and Run-wide normalization remain outside those short database transactions.

The Worker obtains `IUnitOfWorkManager` from the active DI scope and calls `Begin(new XiHanUnitOfWorkOptions(isTransactional: true))`; it awaits all three repositories before `CompleteAsync`. This is implemented source behavior. It is not yet a substitute for proving the exact framework enlistment and rollback behavior against the deployed XiHan.Framework 4.8.0 runtime and PostgreSQL.

### New Candidate path

1. Normalize Candidate data and build Evidence from the original `CrmProspectingRawEnvelope` before opening the transaction.
2. Begin a short transactional UnitOfWork.
3. Insert Candidate and use its generated `BasicId`.
4. Insert eligible Evidence rows.
5. Evaluate and insert the conservative first Score snapshot. Unknown procurement, organization verification, product-fit, and market-fit facts remain false rather than inferred.
6. Complete the UnitOfWork.
7. Only after successful completion, update in-memory identity sets and counters.

### Existing Candidate repair path

1. Reload persisted Evidence and compare the complete Evidence identity components used by the unique index.
2. Read the latest Score history and determine whether the initial Score snapshot is absent.
3. If Evidence or Score is missing, begin a short transaction.
4. Insert only the currently missing Evidence and/or the first Score snapshot.
5. Complete the UnitOfWork.

This repairs legacy partial rows and retries after a prior process interruption. It does not yet recover automatically from a concurrent unique-key collision.

## Transaction and conflict boundary

A database uniqueness violation can abort the current transaction. The Worker must not catch such an exception inside the failed UnitOfWork and continue querying or inserting through the same transaction/connection.

Current behavior on a persistence exception is to let it leave the per-record transaction and reach the Worker's outer failure handler. The handler marks the Run Failed (unless cancellation takes precedence). Startup recovery requeues Running Runs, not Failed Runs. This is deliberately fail-closed, but it means an Evidence collision is not yet an automatically recovered idempotent success.

Required implementation before claiming automatic collision recovery:

1. Let the failed UnitOfWork fully dispose and roll back.
2. Classify the collision narrowly as the Evidence identity unique constraint. Do not treat every SqlSugar/database exception as a duplicate.
3. Start a fresh UnitOfWork/scope and reload rows under the tenant-scoped Candidate.
4. Compare the exact identity tuple: TenantId, CandidateId, ProviderCode, ProviderRecordId, FieldName, SourceUrlHash, and RawPayloadHash. The first two are the tenant/Candidate scope; the remaining five are the Evidence natural identity.
5. Treat the collision as idempotent only when every expected row is now present with the exact identity.
6. Re-check Score history because the failed transaction may have rolled back both Evidence and Score.
7. Insert only still-missing rows in a new transaction, with a small bounded retry count. Propagate unrelated database failures.
8. Add independent-connection PostgreSQL tests for the actual race and recovery behavior.

The SQLite evidence integration suite now verifies exact duplicate rejection, identity dimensions, tenant separation, and a rollback-before-reconciliation preflight. These are test sources only until executed. The rollback preflight does not implement automatic recovery and does not prove PostgreSQL's aborted-transaction behavior.

## Candidate identity and partial retry behavior

Candidate uniqueness is tenant- and Run-scoped over the provider/external-record identity. The Worker uses a typed `(ProviderCode, ExternalId)` tuple with ordinal, case-sensitive equality to mirror the database identity semantics.

- In-memory identity sets are optimizations, not concurrency guarantees.
- Candidate uniqueness does not by itself make Evidence/Score writes idempotent.
- The current per-record transaction prevents a newly inserted Candidate from being committed without its eligible Evidence/first Score when that transaction fails.
- Existing Candidate repair is useful for older or partial rows, but its concurrent unique-key collision still fails the Run as described above.
- The same provider identity may appear in separate Runs unless a separate cross-Run duplicate policy is introduced.

## Score and evidence policy

- Evidence is built from the original provider envelope, not reconstructed from normalized Candidate fields.
- Ingestion is not verification; newly ingested Evidence remains unverified.
- Unsafe or missing source URLs do not produce traceable Evidence.
- Do not hash low-entropy contact names or email addresses without an appropriate keyed scheme.
- Do not infer procurement intent, product fit, market fit, or verified organization identity from generic summaries.
- A Score or priority never approves a Candidate, converts a Lead, sends outreach, or publishes website content.
- Human review remains required before a Candidate enters the formal CRM Lead/customer workflow. External email and website publishing retain their separate manual approval gates.

## PostgreSQL acceptance matrix

The integration suite must run against PostgreSQL with independent connections (and independent scopes for race tests):

| Scenario | Required assertion |
|---|---|
| Normal write | Candidate + eligible Evidence + first Score are visible after commit |
| Evidence insert failure | Candidate and partial Evidence are rolled back |
| Score insert failure | Candidate and Evidence are rolled back |
| Cancellation before commit | No partial record set remains |
| Existing Candidate repair | Only missing Evidence/Score is added |
| Concurrent Evidence identity insert | One writer wins; loser reloads in a fresh transaction and confirms exact identity before treating it as idempotent |
| Concurrent Candidate identity insert | Exactly one Candidate wins; loser reloads the winner and reconciles child rows |
| Separate Runs | Same provider identity may exist in both Runs unless a deliberate cross-Run rule is added |
| Tenant isolation | Tenant A cannot read, attach, or mutate Tenant B Evidence/Score |
| Missing/unsafe source | No Evidence and no evidence-quality points |
| Review read API | Evidence/Score is returned only after campaign owner/data-scope authorization |

SQLite tests are useful repository preflight checks but do not prove PostgreSQL transaction, locking, or concurrency behavior.

## Run creation and worker ownership boundaries

Run creation checks the tenant-scoped RequestKey before insert and validates that a repeated key has the same payload hash. A concurrent same-key insert is still protected by the unique index, but the losing request is not yet guaranteed to reload and return the winning Run. Add narrow conflict recovery and an independent-connection PostgreSQL test before describing concurrent Run creation as fully idempotent.

Keep exactly one active Worker deployment until database-authoritative lease/fencing is implemented. The current startup recovery broadly requeues Running Runs and has no lease owner, heartbeat, expiry, or fencing token. Process-local concurrency limits and conditional Queued-to-Running claim are not distributed ownership.

## Separate validation gates

Report these independently; do not infer one from another:

1. Source inspection and source-level regression test coverage.
2. .NET restore/build.
3. Unit and SQLite integration tests.
4. PostgreSQL migrations and independent-connection integration tests.
5. Windows native pull, build, startup, and runtime acceptance.

As of this audit, no current CI workflow run has been observed for the latest branch head. The newly added SQLite tests have not been executed in this environment. PostgreSQL migrations/concurrency tests and Windows native acceptance remain open. PR #1 remains Draft and must not be merged before the required gates pass.
