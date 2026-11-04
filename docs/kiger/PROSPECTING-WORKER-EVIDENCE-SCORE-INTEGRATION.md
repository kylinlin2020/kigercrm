# Prospecting Worker Evidence and Score Integration

Status: implementation plan derived from the current `CrmProspectingRunHostedService` and the evidence/score persistence primitives. This document does not claim that the worker currently writes evidence or scores.

## Current worker behavior

The worker currently:
1. Claims a queued Run.
2. Calls the configured providers and collects raw envelopes.
3. Serializes the raw envelopes into the Run result.
4. Loads existing Candidates and checks duplicates against existing Leads and Candidates in the Run.
5. Inserts each new Candidate separately.
6. Completes the Run.

The Candidate insertion loop does not currently call `CrmProspectingEvidenceFactory`, does not insert evidence rows, and does not persist a score snapshot. Candidate inserts are individually awaited; the entire normalization pass is not one atomic transaction.

## Required per-record persistence boundary

Do not wrap the full 2,000-record run in one long database transaction. Provider network calls must remain outside database transactions. For each accepted raw envelope:

1. Normalize and validate the Candidate fields.
2. Build the Candidate entity.
3. Build evidence rows from the original `CrmProspectingRawEnvelope` using the evidence factory. Preserve the original provider code and record provenance; do not derive evidence from the normalized Candidate alone.
4. Determine score inputs only from persisted/validated evidence and explicit procurement/product/market facts. Unknown values must remain false/null. Never infer a direct procurement signal from an AI summary or generic company industry.
5. Begin a short database unit of work.
6. Insert Candidate and obtain its generated ID.
7. Set that ID on every evidence row and insert evidence rows.
8. If score inputs meet the evidence contract, evaluate the deterministic score and insert the immutable score snapshot.
9. Complete the unit of work.
10. Only after commit, update in-memory duplicate sets and counters.

If any insert fails, roll back the Candidate, Evidence, and Score together. A later retry must be safe: provider identity + external ID uniqueness/idempotency must be enforced by a tenant-scoped database constraint or a conditional insert/upsert contract, not only an in-memory HashSet.

## Framework API proof gate

Before wiring the transaction, verify the exact v4.8.0 APIs available in this repository for:
- Resolving `IUnitOfWorkManager`.
- Beginning and completing/disposing a unit of work.
- Ensuring all repositories in the unit of work resolve the same SqlSugar client/transaction.
- Obtaining the generated Candidate primary key after insert.
- Whether `AddAsync` participates in the active unit of work.

Do not invent overloads or rely on a guessed transaction API. Add a focused integration test using the actual PostgreSQL provider and independent connections to prove rollback of all three rows when Evidence or Score insertion fails.

## Evidence policy

- Evidence factory output remains `unverified`; ingestion does not equal verification.
- A record with no safe HTTPS source URL produces no evidence. Such a Candidate must not receive evidence-quality points and should be held for review.
- Do not create evidence rows for fields not present in the source record.
- Do not store raw contact names or email addresses in evidence hashes. The Candidate remains the operational record under existing access controls.
- Reject unsafe URLs before any future provider fetch. URL validation in the evidence factory is not a substitute for a shared outbound HTTP SSRF boundary.
- Store provider record ID, source URL, retrieval time, publication time, evidence type, and raw payload hash where available.

## Score policy

The scorecard is a prioritization aid, not a purchase-probability estimate. The current scorecard must not be applied by fabricating booleans from incomplete provider records.

Initial safe integration should:
- Persist a score snapshot only when all score inputs are supported by explicit facts/evidence.
- Persist hard review gates even when score is high.
- Never let a high score bypass PendingReview or Duplicate status.
- Never trigger email, CRM outreach, or website publication.
- Preserve scorecard version and evaluation timestamp for reproducibility.

## Completion criteria

- Candidate, Evidence, and Score commit or roll back together.
- Candidate identity is idempotent under retries and concurrent workers.
- Tenant A cannot read or link Tenant B evidence/scores.
- A missing/unsafe source never receives evidence-quality points.
- Score components sum to total and satisfy the versioned scorecard bounds.
- Cancelled Runs do not start additional records after cancellation is observed.
- PostgreSQL integration tests cover success, evidence insert failure, score insert failure, retry, duplicate race, and tenant isolation.
- Windows pull/build/run acceptance remains a separate required gate.


## Reviewer read API progress

The application service now exposes `GetCandidateReviewDetailAsync(candidateId)` through `ICrmProspectingAppService`. It:
- Loads the Candidate, its Run, and its Campaign.
- Applies the existing campaign owner/data-scope authorization check before returning review material.
- Reads tenant-scoped Evidence rows and the latest 20 immutable Score snapshots.
- Returns source URL, provider record identity, verification status, evidence type, retrieval/publication timestamps, and hashes alongside the Candidate and score component breakdown.
- Does not change Candidate status, approve a candidate, convert a Lead, or trigger outreach.

This is an API/read-model capability only. A dedicated frontend review screen and the Worker write path are still outstanding. Until the Worker begins persisting Evidence and Score rows, the new endpoint may legitimately return empty evidence/score collections for older or not-yet-integrated candidates.


## Candidate review API and frontend progress

The backend review-detail read path is now exposed as `GetCandidateReviewDetailAsync(candidateId)`. It authorizes through the Candidate → Run → Campaign ownership/data-scope chain, then returns the Candidate, tenant-scoped evidence rows, and the latest 20 immutable score snapshots. The frontend API client now has a typed `candidateReviewDetail` method, and the prospecting page exposes a reviewer panel for score breakdowns, review gates, evidence field metadata, verification state, and source links. English and Simplified Chinese labels have been added.

A source-level backend wiring guard now checks the permission attribute, campaign data-scope authorization, evidence/score repository reads, and DTO mapping. The frontend API route test covers the new GET route. These tests have been added but have not been executed in this environment.

The module follows the repository's documented convention: `SaasRepository` implementations and application services are convention-registered by the framework. The new evidence/score repositories therefore do not need duplicate explicit registrations in the CRM module. This convention still needs runtime DI resolution verification during build/startup testing.

## Retry and concurrency gap discovered during implementation review

The existing Candidate unique key is tenant + Run + Provider + ExternalId + IsDeleted. It protects the normalized Candidate identity within one Run, but it does not by itself make the three-record persistence sequence idempotent.

A failure window currently exists in the Worker design: if Candidate insertion succeeds and a later Evidence/Score write fails, a retry can load that Candidate into the existing-key set and skip it. Unless retry processing explicitly repairs missing child rows, the Candidate can remain permanently without its provenance or score snapshot. The current Worker does not yet write Evidence or Score, so this is a design constraint to close before enabling those writes.

Required implementation decisions:
- Do not treat an existing Candidate as a completed ingestion record. On retry, reconcile its expected Evidence set and score snapshot, or persist a per-envelope processing state that can be resumed safely.
- Define a deterministic evidence idempotency key from stable source identity (candidate, provider, field, canonical source URL, and payload hash where present). Preserve distinct observations when the payload hash or retrieval event differs; do not collapse legitimate history.
- Make the score snapshot idempotency key include candidate + scorecard version + source/evaluation fingerprint, rather than relying on a process-local check.
- Keep the Candidate/Evidence/Score writes in one short transaction. If framework transaction participation cannot be proven, do not enable partial writes.
- Add independent PostgreSQL connection tests for: candidate committed but evidence absent retry repair; evidence insert rollback; score insert rollback; concurrent duplicate envelope; repeated same envelope; and cross-tenant foreign-key rejection.

The 5.9.18 and 5.9.19 migration guards now have source-level coverage for tenant/candidate composite foreign keys and score bounds. These tests are source assertions only and have not been executed. They do not prove the SQL runs successfully on PostgreSQL.

## 2026-11-04 implementation audit: concrete Worker call path

A fresh source audit of `CrmProspectingRunHostedService` confirms the following implementation details:

- The Worker resolves `ICrmProspectingCandidateRepository` and `ICrmLeadRepository` for normalization. It does not resolve either evidence/score repository, either persistence factory, or `IUnitOfWorkManager`.
- Each new Candidate is persisted with a standalone `await candidates.AddAsync(candidate, cancellationToken)`.
- The in-memory `existingKeys` set is populated from the Run's existing Candidate rows. When a key is already present, the loop immediately continues; it does not reconcile or repair child Evidence/Score rows.
- The Evidence and Score repositories inherit `SaasRepository<TEntity>` and receive `ISqlSugarClientResolver`; this proves their repository construction pattern, but does not prove that separately resolved repository instances share one transaction.
- The CRM module project file references the SaaS project and imports shared build/NuGet property files. The transaction manager implementation is not defined in the CRM module source audited here. The exact package version/API and transaction participation therefore remain unproven.

### Safe implementation gate

Do not wire the three writes by simply resolving three repositories and assuming a shared transaction. First identify the exact framework package/version and inspect its UOW manager and SqlSugar resolver implementation. Then add a PostgreSQL integration test that opens the same application scope, inserts a Candidate, deliberately fails Evidence or Score insertion, and verifies through a separate database connection that all writes rolled back.

The current per-Run Candidate unique identity is useful protection against duplicate Candidate rows, but it also makes the existing-key retry branch especially important: once child persistence is enabled, that branch must invoke a reconciliation path rather than skip the envelope. Reconciliation must distinguish an already-complete envelope from a partially persisted one using database state and stable source identity.

No source-level review in this audit is represented as a successful compile, unit test, PostgreSQL integration test, or Windows runtime acceptance.

## 2026-11-04 run-state concurrency audit

A separate audit of `CrmProspectingRunRepository` and its contract confirms:

- `ClaimQueuedAsync` uses a conditional Queued -> Running update, so duplicate queue deliveries cannot both claim the same currently queued row.
- `CompleteAsync` and `FailAsync` only perform their normal terminal transition while the row is Running; if cancellation has changed it to CancelRequested, they transition it to Cancelled instead.
- `RecoverInterruptedAsync` updates every Running row in the current tenant scope back to Queued, and every CancelRequested row to Cancelled. It does not identify which process owns a Run, nor check an expiry/heartbeat.
- The repository contract explicitly limits this recovery operation to single-active-worker startup and says it must not run concurrently with live workers.

### Deployment constraint and required next step

The queue claim is a useful duplicate-delivery guard, but it is not a distributed worker lease. If a second application instance starts and executes startup recovery while the first instance is still processing, the second can requeue a live Run. A subsequent worker may claim it and repeat provider calls / normalization while the first process continues. The conditional candidate unique index may reject duplicate inserts, but it does not prevent duplicate external provider calls or protect Run completion from a stale worker.

Until a database-authoritative lease with owner identity, expiry, heartbeat, and monotonically increasing fencing generation is implemented and tested, deploy exactly one active prospecting Worker instance and do not run startup recovery concurrently with a live worker. The Redis queue remains a delivery hint, not the source of ownership truth.

Required PostgreSQL acceptance cases: two independent worker processes racing to claim; startup recovery during a live claim; expired lease takeover; stale owner attempting Candidate insert and Run completion after takeover; cancellation racing with completion; and restart after partial normalization. These tests must use independent database connections and verify persisted state, not only in-memory locks or SQLite behavior.

This audit is source inspection only. It is not evidence of a passing multi-process test, PostgreSQL test, build, or Windows runtime check.
