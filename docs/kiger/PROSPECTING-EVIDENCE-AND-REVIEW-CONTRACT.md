# Prospecting evidence and human-review contract

Date: 2026-11-04

## Status and scope

This document defines the persistence and review contract required before a live prospecting provider is considered production-ready. It is an implementation specification, not a claim that the schema, adapters, or UI described below are already implemented.

The current provider contract returns a flat `CrmProspectingRawRecord`; `CrmProspectingRawEnvelope` retains only the provider code and record. Candidate persistence retains a source string and summary, but does not yet provide a durable field-level provenance trail. This is insufficient for an operator to independently verify why a company was selected or where a contact detail originated.

The workflow remains:

`provider discovery -> immutable raw evidence -> normalization/deduplication -> candidate review -> explicit approval -> existing CRM conversion`

Provider code must never create Leads directly. No automated email, messaging, or external outreach is in scope.

## Evidence model

Each provider result must be represented as an immutable evidence envelope with:

| Field | Required | Meaning |
|---|---:|---|
| ProviderCode | yes | Stable adapter identifier, normalized and bounded |
| ProviderRecordId | preferred | Stable ID assigned by the upstream source |
| ExternalId | yes | Stable adapter-level deduplication key |
| SourceUrl | yes when the source exposes a page | Canonical HTTPS page/API record URL, validated before persistence |
| RetrievedAtUtc | yes | Retrieval instant recorded by the application |
| ProviderPublishedAtUtc | no | Upstream publication/update time when explicitly supplied |
| EvidenceType | yes | Registry, tender, award, importer/trade, search result, enrichment, or other controlled value |
| EvidenceSummary | yes | Short factual extract, not an AI-generated assertion |
| RawPayloadHash | yes | SHA-256 of the bounded canonical raw payload |
| RawPayload | policy-dependent | Bounded provider payload; redact secrets and unnecessary personal data |
| FieldEvidence | yes | Per-field provenance entries for values surfaced to reviewers |

A field evidence entry contains:

- `field`: stable field key, e.g. `companyName`, `website`, `businessEmail`, `contactName`, `purchaseSignal`.
- `value`: the observed value, or a reference to the normalized candidate field.
- `providerCode`, `providerRecordId`, `sourceUrl`, `retrievedAtUtc`.
- `evidenceKind`: `direct`, `registry`, `provider_asserted`, or `inferred`.
- `verificationStatus`: `unverified`, `corroborated`, `verified`, or `rejected`.
- `confidence`: optional numeric score only when the provider supplies a documented score or a versioned deterministic rule computes it. AI confidence must not be presented as source verification.

Unknown values stay null. A provider's assertion is not automatically a verified fact. AI may summarize evidence but must not create provenance or upgrade verification status.

## Candidate review requirements

The review detail must make the evidence inspectable without requiring the reviewer to trust an AI score:

1. Show company identity, website and contact fields separately from evidence.
2. Show the source name, source URL, provider record ID and retrieval time for each important field.
3. Distinguish direct source evidence from provider-asserted and inferred values.
4. Show the procurement/trade signal itself: buyer/issuer, tender or award identifier, title, publication date, jurisdiction, product/category match and original source link when available.
5. Show conflicts instead of silently choosing one value. Conflicting email, website, company name or country evidence must be reviewable.
6. Keep candidate status independent of evidence verification. A candidate is not approved merely because it has a high score or complete-looking contact fields.
7. Record reviewer, decision, decision time and an optional reason. Rejected evidence must remain auditable; it must not be silently overwritten by a later provider response.

The conversion operation continues to use the existing transactional candidate-to-Lead flow. Conversion must require an explicit approved candidate state and preserve links to the evidence record in CRM audit/history where the current schema permits. It must not trigger outreach.

## Provider classes and evidence policy

### Tier 1: official identity and public procurement

Prefer official registries and public tender/award APIs. For the generator and water-pump pilot, prioritize evidence of actual procurement or importer activity over generic industry labels.

Examples for evaluation (not yet implemented): EU TED, World Bank procurement, USASpending, UK Contracts Finder, GLEIF, Companies House and other official country registries. Each adapter must document geographic coverage, license/terms, pagination, update cadence, stable identifiers and source URLs before enablement.

### Tier 2: search discovery

Serper, Brave and SerpAPI are discovery channels. Search snippets may identify a company or a page to inspect, but must not be represented as verified contact details. Store the result URL and query context; do not scrape login-gated, paywalled, CAPTCHA-protected or access-controlled pages.

### Tier 3: paid enrichment

Apollo, People Data Labs and Hunter remain optional. Require customer-provided credentials, explicit enablement, per-provider quota/cost limits and visible usage accounting. Do not silently incur charges. A paid provider's returned email remains provider-asserted until the configured verification policy says otherwise.

### AI synthesis

AI is a transformation layer, not a source. Store model/provider, prompt or ruleset version, generation timestamp and references to the exact evidence IDs used. Never use AI-generated contact data as a verified value. AI-generated company fit or purchase-intent summaries must be labelled as analysis.

## Network and data safety

All outbound adapters must use a shared HTTP execution boundary:

- HTTPS only, explicit provider host allowlists and redirect validation on every hop.
- Reject loopback, private, link-local, multicast and reserved IP ranges after DNS resolution; defend against DNS rebinding by validating the resolved destination at connection time where supported.
- Do not fetch arbitrary URLs found in provider payloads without the same URL/IP checks.
- Bounded connect/overall timeouts, response byte limits, pagination limits, retry budgets and cancellation.
- Typed errors: authentication, quota, rate-limit, transient network, invalid response, policy rejection and permanent provider failure.
- Never log API keys, authorization headers, raw secrets or full personal-data payloads.
- Tenant-scoped provider configuration and encrypted secrets; never accept provider credentials from a prospecting Run payload.
- Respect provider terms, robots/access policies and applicable privacy/data-retention requirements.

## Persistence and migration sequence

Do not add only DTO fields and call the evidence feature complete. The schema, migration, fresh-tenant CodeFirst path, repository mapping and review API must land together.

1. Introduce an evidence entity/table keyed by tenant, Run and Candidate, with immutable source metadata and a bounded JSON evidence document.
2. Add a stable Candidate-to-evidence relationship and indexes for tenant/run/candidate/provider/source record.
3. Add upgrade migration and fresh-tenant CodeFirst parity tests. Existing candidates must remain valid with an explicit `legacy_unattributed` evidence state; do not fabricate source URLs or retrieval timestamps.
4. Persist evidence in the same transaction as candidate creation/update. Duplicate candidate merges must append evidence rather than erase previous provenance.
5. Add tenant-scoped read APIs and permission checks for evidence details.
6. Add review UI with source links and field-level verification status.
7. Add retention/redaction policy and audit events for reviewer decisions.
8. Only then enable a real provider in a non-production pilot.

Raw payload retention must be configurable and minimized. A hash and selected evidence fields should be preferred over retaining entire payloads when full payload retention is not required by the provider terms or business use case.

## Acceptance tests

- A raw provider response creates a candidate and linked evidence records atomically.
- Failure between candidate and evidence writes rolls back both.
- Repeated provider delivery is idempotent for the same provider record while preserving newly observed evidence versions.
- Conflicting provider values are retained and visible; no silent overwrite.
- Tenant A cannot read, update, approve or convert Tenant B evidence/candidates.
- Missing source URL, provider record ID or contact fields remain explicitly missing rather than AI-invented.
- URL validation blocks private/loopback/link-local destinations and unsafe redirects.
- Response byte, page, record, timeout and retry caps are enforced.
- Paid-provider quota exhaustion is visible and does not trigger unbounded retries.
- Candidate approval and conversion require explicit human action and existing CRM permissions.
- No test or adapter sends email or other outreach.
- Upgrade migration and fresh-tenant CodeFirst produce equivalent evidence schema.

## Delivery gate

A provider is not production-ready until its terms/policy review, adapter contract tests, evidence persistence, tenant isolation, quota/error behavior, review UI and end-to-end tests pass. Source inspection and fake-provider smoke tests do not satisfy this gate.

The first live pilot should use one official procurement/registry source with read-only credentials and a small, manually reviewed dataset. Paid enrichment and automated outreach remain disabled.
