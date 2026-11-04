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
