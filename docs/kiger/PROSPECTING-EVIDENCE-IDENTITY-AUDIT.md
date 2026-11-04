# Prospecting Evidence Identity Audit

## Scope

This audit compares the identity and retry rules used by the prospecting worker, Candidate rows, and field-level Evidence rows. It is a source review only; no runtime or database test is implied.

## Confirmed mismatch

The worker currently derives Candidate.ExternalId as follows:

- If the provider ExternalId is blank, it hashes the JSON serialization of the raw record.
- Otherwise it trims the provider ExternalId.
- If the resulting value exceeds 200 characters, it stores `sha256:` plus the SHA-256 hash of the trimmed value.

The Evidence factory currently derives Evidence.ProviderRecordId differently:

- A blank provider ExternalId becomes an empty string.
- A nonblank value is clipped to 240 characters.
- It does not apply the Candidate worker's trim/hash rules.

Consequences:

1. Evidence cannot reliably be correlated to the Candidate identity for blank, whitespace-padded, or overlong provider IDs.
2. A retry can compute a different Evidence identity from the Candidate's stored ExternalId.
3. The current factory test that expects an empty ProviderRecordId for a blank provider ID locks in the mismatch instead of testing cross-entity identity parity.

## Additional payload identity gap

Evidence.RawPayloadHash is currently set to the uppercase provider-supplied SHA-256 only when the provider value is valid; otherwise it is stored as an empty string.

The Evidence unique identity includes CandidateId, ProviderCode, ProviderRecordId, FieldName, SourceUrlHash, and RawPayloadHash. When providers omit a valid payload hash, changed provider content can therefore reuse the same empty-hash identity and be mistaken for already persisted evidence during reconciliation.

## Required correction

Implement one shared domain-level identity helper and use it from both the worker and Evidence factory:

- Normalize provider ExternalId by trimming once.
- For a blank ExternalId, derive a deterministic SHA-256 identity from a canonical serialization of the complete raw record.
- For an ExternalId longer than the Candidate column limit (200), store `sha256:` plus SHA-256 of the trimmed value.
- For ordinary IDs, preserve the trimmed value.
- Derive a payload fingerprint from the provider's valid SHA-256 when supplied; otherwise derive SHA-256 from the same deterministic serialization.
- Store the same normalized provider record identity in Candidate.ExternalId and Evidence.ProviderRecordId.
- Keep personal data out of unsalted field-value hashes; the payload fingerprint is a record-level integrity identifier, not a searchable hash of individual contact fields.

The canonical serialization must be deterministic across process restarts and deployments. If JSON property ordering or serializer options can vary, define and test explicit serialization options or a stable canonical representation before relying on the digest.

## Required regression tests

1. Blank ExternalId: Candidate identity equals Evidence.ProviderRecordId.
2. Whitespace-padded ExternalId: both identities use the same trimmed value.
3. ExternalId longer than 200 characters: both identities use the same `sha256:` identity.
4. Same provider ID and source URL but changed record content with no provider payload hash: a new evidence identity is produced.
5. Same record retried unchanged: evidence reconciliation remains idempotent.
6. Provider-supplied valid SHA-256: the supplied normalized digest is retained.
7. Unicode and reordered JSON/property values: deterministic identity behavior is explicitly tested.

## Separate concurrency requirement

Evidence insertion currently uses an application-level read-then-insert reconciliation. The database unique index remains the final concurrency boundary. A concurrent unique-key violation must be handled by rolling back the failed unit of work, re-reading the persisted evidence set, and treating only exact identity matches as successful reconciliation. Do not swallow arbitrary database errors as duplicate conflicts.

## Operational constraints

- Keep the prospecting worker single-instance until lease ownership and fencing are implemented and verified against PostgreSQL with independent connections.
- Do not persist scores from inferred or untraceable facts. Score persistence must be based on explicit facts with provenance and review gates.
- This audit does not certify compilation, tests, or migration execution.
