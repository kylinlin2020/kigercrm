# Prospecting provider operations

## Official USAspending adapter

- Provider code: `usaspending_awards`
- Fixed endpoint: `https://api.usaspending.gov/api/v2/search/spending_by_award/`
- Authentication: none
- Coverage: US federal award records; not a general company directory.
- Current behavior: first page, up to 20 awards per Run; warns when additional results exist.
- Evidence: award URL, retrieval timestamp, payload SHA-256, `official_procurement`.
- Safety: fixed HTTPS host/path, redirects disabled, 25-second timeout, 6 MiB response cap.

Enable with `XiHan:CRM:Prospecting:EnableUsaSpendingProvider=true`.

## Official UK Find a Tender adapter

- Provider code: `uk_find_tender`
- Fixed endpoint: `https://www.find-tender.service.gov.uk/api/1.0/ocdsReleasePackages`
- Authentication: public OCDS consumption API; no eSender API key is used.
- Coverage: UK Find a Tender OCDS releases, filtered to tender and award stages and a rolling 30-day updated window.
- Current behavior: up to 3 cursor pages, 100 releases per page; results are locally matched against product/search/ICP keywords.
- Buyer mapping: only parties with the OCDS `buyer` role are treated as prospective buying organisations. A supplier party is never promoted as a buyer.
- Evidence: official notice URL, publication time, payload SHA-256, `official_procurement`; publicly published buyer website/contact fields are carried only when present and valid.
- Safety: fixed HTTPS host/path, redirects disabled, 30-second timeout, 6 MiB response cap, cursor link host/path validation. HTTP 429/503 is surfaced with Retry-After guidance and is not blindly retried.

Enable with `XiHan:CRM:Prospecting:EnableFindTenderProvider=true`.

## GLEIF legal-entity identity enrichment

- Resolver: `CrmGleifIdentityResolver`
- Official API: `https://api.gleif.org/api/v1/lei-records`
- Authentication: none; JSON:API response.
- Purpose: resolve an already-discovered Candidate's legal name to an LEI record. This is not a discovery provider and is never used to search generic product keywords.
- Matching: normalized legal name must match exactly and there must be exactly one result among the first 10 registry matches. Ambiguous, partial or fuzzy matches return no identity.
- Stored provenance: LEI is stored as the registry record identity; evidence stores a hash of the LEI plus the official API record URL, raw record hash, entity status, LEI registration status, country and registered identifier in the evidence summary.
- Safety: fixed HTTPS host/path, redirects disabled, 20-second timeout, 2 MiB response cap, descriptive User-Agent, and at most 20 candidate lookups per Run.
- The evidence remains `unverified` and requires human review. `ACTIVE` entity status and `ISSUED` LEI registration status are reported separately; neither is treated as proof of buyer intent.

Enable with `XiHan:CRM:Prospecting:EnableGleifIdentityResolver=true`.

## Worker lease recovery status

- Tenant startup recovery now invokes the PostgreSQL-clock-based RecoverExpiredLeasesAsync before rebuilding the Redis queue. Expired lease-backed Running Runs are requeued; expired CancelRequested Runs are finalized as Cancelled.
- This is a recovery primitive only. The active Worker still uses the legacy claim/write/terminal path; lease claims, child-write fencing, heartbeat/renewal and fenced terminal transitions are not yet wired end-to-end.
- Keep worker concurrency at one and do not deploy multiple active application workers until the full lease lifecycle is integrated and PostgreSQL concurrency acceptance passes.

## Shared data-handling rules

- Procurement notices are evidence of buyer activity, not proof of fit or purchase intent.
- Public buyer contacts are source-published data; they still require validation before outreach.
- No provider creates or updates CRM Leads directly.
- Human approval is required before Candidate conversion to the existing CRM Lead workflow.
- No outbound email or website publication is automated by these providers.
- The fake provider is separate and remains opt-in via `XiHan:CRM:Prospecting:EnableFakeProvider`.

## Known limitations and next work

- USAspending reads one page only.
- Find a Tender reads at most three pages and searches only a rolling 30-day update window. It is not an exhaustive historical archive.
- Neither adapter currently persists a dedicated provider request/cost ledger or shared response cache.
- The GLEIF LEI API is a legal-entity registry and identity-resolution source, not a lead-discovery directory. Its planned integration should enrich/verify already discovered candidate names and must preserve entity status, LEI registration status, country and registered identifier separately.
- Unit tests are committed but must still be compiled and run in the .NET 10 environment. Live API and Windows/PostgreSQL end-to-end acceptance remain outstanding.
