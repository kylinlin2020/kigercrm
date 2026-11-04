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
