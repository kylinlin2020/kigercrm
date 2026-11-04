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

## Official EU TED adapter

- Provider code: `eu_ted`
- Fixed endpoint: `https://api.ted.europa.eu/v3/notices/search`
- Authentication: anonymous public Search API; no API key is used.
- Query: full-text product keywords combined with a 30-day publication-date window.
- Current behavior: up to 2 pages × 100 notices per Run; buyer and publication number are required before a Candidate record is emitted.
- Buyer mapping: TED `buyer-name` is used as the buyer organisation; no contact email or website is inferred.
- Evidence: official TED notice detail URL, publication time, payload SHA-256, `official_procurement`.
- Safety: fixed HTTPS host/path, redirects disabled, 30-second timeout, 6 MiB response cap, and HTTP 429/503 surfaced with Retry-After guidance.

Enable with `XiHan:CRM:Prospecting:EnableTedProvider=true`.

## Official UK Contracts Finder adapter

- Provider code: `uk_contracts_finder`
- Fixed endpoint: `https://www.contractsfinder.service.gov.uk/Published/Notices/OCDS/Search`
- Authentication: public OCDS search API; no API key is configured.
- Coverage: UK Contracts Finder published notices, using the API's publication-date and stage filters over a rolling 30-day window.
- Current behavior: up to 3 pages × 100 releases; locally matched against product/search/ICP keywords.
- Buyer mapping: only OCDS parties explicitly carrying the `buyer` role are used for buyer contact details. Supplier-only parties are not promoted as buyers.
- Evidence: official Contracts Finder notice URL, publication time, payload SHA-256, `official_procurement`.
- Safety: fixed HTTPS host/path, redirects disabled, 30-second timeout, 6 MiB response cap, validated pagination links, HTTP 429/503 surfaced with Retry-After, and HTTP 403 treated as a source rate-limit response requiring at least a five-minute cooldown (no blind in-request retry).

Enable with `XiHan:CRM:Prospecting:EnableContractsFinderProvider=true`.

## World Bank procurement dataset — research-only

The Finances One `DS00979` dataset is assessed but is not registered as a CRM Provider yet. Its public schema contains bid description, project, country, procurement category/method, dates and notice URL, but does not expose a buyer organisation/contact field. The current worker falls back from missing CompanyName to Title, so direct ingestion would risk turning a tender description into a fake company. See `docs/kiger/WORLDBANK-PROCUREMENT-ADAPTER-DESIGN.md` for the verified API, schema boundary and safe second-stage implementing-agency resolution requirements.

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

## Worker lease lifecycle status

- The active Worker claims Runs using PostgreSQL lease token + fencing version; child writes and terminal transitions are fenced.
- A separate two-minute heartbeat uses a fresh tenant-aware DI scope and repository instance. Work-boundary renewals remain as defense in depth.
- If the heartbeat sees `CancelRequested`, it stops renewing without cancelling the processing token so the worker can finalize via the fenced Cancel transition. Lease loss or heartbeat errors cancel the linked Run token.
- Startup recovery invokes PostgreSQL-clock-based `RecoverExpiredLeasesAsync` before Redis queue reconstruction.
- Keep worker concurrency at one and do not deploy multiple active application workers until PostgreSQL dual-connection race, crash-recovery and cancellation acceptance passes.

## Shared data-handling rules

- Procurement notices are evidence of buyer activity, not proof of fit or purchase intent.
- Public buyer contacts are source-published data; they still require validation before outreach.
- No provider creates or updates CRM Leads directly.
- Human approval is required before Candidate conversion to the existing CRM Lead workflow.
- Candidate deduplication checks exact provider/external identity, normalized email and website host across earlier non-rejected Runs, in addition to same-Run duplicates and existing CRM Leads. Rejected prior Candidates do not block rediscovery.
- No outbound email or website publication is automated by these providers.
- The fake provider is separate and remains opt-in via `XiHan:CRM:Prospecting:EnableFakeProvider`.

## Known limitations and next work

- USAspending reads one page only (up to 20 awards).
- Find a Tender and Contracts Finder each read at most three pages and search a rolling 30-day window. Neither is an exhaustive historical archive.
- TED reads at most two pages × 100 notices per Run and searches a rolling 30-day publication window.
- World Bank Finances One exposes a large procurement-notice dataset, but the published dataset schema currently exposes bid description, project, country, dates and notice URL—not a buyer organisation/contact field. It must remain research-only until a separate, source-backed project/implementing-agency resolution step is designed; do not turn bid descriptions into fake company names.
- A persistent provider request/cost ledger, shared response cache, cross-Run coverage cursor and durable rate-limit circuit breaker remain outstanding. Cross-Run Candidate deduplication by provider identity/email/website is now implemented, but it does not yet maintain a first-class observation-to-prior-Candidate link or source coverage cursor.
- GLEIF is a legal-entity identity-resolution source, not a discovery directory; it enriches already-discovered candidates and preserves entity status, LEI registration status, country and registered identifier separately.
- Test source is committed but still needs compilation and execution in the .NET 10 environment. Live API and Windows/PostgreSQL end-to-end acceptance remain outstanding.

## Request ledger and incremental coverage roadmap

The detailed implementation specification is maintained in [`PROSPECTING-REQUEST-LEDGER-AND-COVERAGE-DESIGN.md`](./PROSPECTING-REQUEST-LEDGER-AND-COVERAGE-DESIGN.md). It separates durable provider-attempt audit, successful-window coverage, disposable response caching, and persisted cooldown/circuit state. These are not yet implemented; do not infer complete coverage or cache hits from current Run summaries. The next implementation slice should start with the request ledger and its PostgreSQL concurrency tests before enabling cursor advancement or caching.
