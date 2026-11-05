# Provider migration priorities

Date: 2026-11-04

This is a migration decision record, not a claim that any live adapter has been implemented.

## Reference inventory confirmed

The source CRM contains these provider families:

- Search discovery: Serper, Brave, SerpAPI.
- Place discovery: Google Places.
- Official company identity: GLEIF, Wikidata, OpenCorporates, Companies House, Netherlands KVK, plus country-specific official registries.
- Enrichment: Apollo, People Data Labs, Hunter.
- Public procurement and trade: EU TED, World Bank procurement, USASpending, UK Contracts Finder, UN Comtrade, US Census trade, Eurostat Comext.
- AI-assisted search: configured AI model provider.

## KIGER migration order

1. **Official identity / registries first**: stable company identifiers and authoritative company evidence; implement only where the API terms and geographic coverage are clear.
2. **Public procurement and trade signals second**: official tender/award/trade APIs can reveal active buyers/importers for generators and water pumps. Store the underlying source URL, provider record ID, retrieval timestamp, and evidence summary.
3. **Search-engine discovery third**: Serper/Brave/SerpAPI are discovery channels, not authoritative evidence. Search snippets must not be represented as verified contact details.
4. **Commercial enrichment last and optional**: Apollo/PDL/Hunter require customer-provided credentials and a separate cost/quota policy. Do not silently incur charges.
5. **AI-assisted synthesis is never a source of truth**: preserve provider evidence separately; unknown email, phone, address and contact fields stay null until independently verified.

## Required adapter contract additions before live providers

The current KIGER contract only returns a flat raw record and warnings. Before integrating multiple sources, extend the design to retain:
- stable provider code and provider record ID;
- canonical source URL and retrieval timestamp;
- evidence fields and field-level provenance;
- provider page/request status, pagination cursor and quota usage;
- retryability and typed error category;
- bounded response size, request timeout, rate limit and cancellation;
- explicit cost/quota metadata for paid providers.

Network security must be enforced centrally: HTTPS allowlists, DNS/IP rebinding protection, block private/link-local/loopback addresses, validate every redirect, cap response bytes, and never fetch arbitrary URLs supplied by provider output without the same checks.

## Product-fit pilot recommendation

For generator and water-pump prospecting, pilot official procurement/tender sources and company registries before paid contact enrichment. Target evidence of purchasing intent (tenders, awarded contracts, importer activity) plus verified company identity. Do not infer that a company buys generators or pumps merely because its generic industry label appears relevant.

## Non-goals

- No provider credentials or paid subscriptions are assumed.
- No scraping behind login, paywall, CAPTCHA, or access controls.
- No automated outreach or email sending.
- No live adapter is considered complete until contract tests, source-policy review, tenant-safe persistence, quota handling, and end-to-end human-review flow pass.

## Current branch reconciliation (2026-11-04)

This decision record predates several adapter and worker increments. The following are now present in `kiger-dev` source and must not be described as merely planned:

- Official procurement adapters: USAspending, UK Find a Tender, EU TED and UK Contracts Finder.
- Official identity enrichment: GLEIF resolver (identity enrichment only; not a discovery provider).
- Provider request-attempt ledger and campaign/provider/scope coverage checkpoint, including atomic PostgreSQL success finalization.
- Cross-Run duplicate screening by provider identity, normalized email and website host.
- PostgreSQL lease claim, independent heartbeat, child-write fencing, terminal fencing and expired-lease recovery in the HostedService.

These are implemented at source level but have not yet passed .NET 10 execution, live-source checks or PostgreSQL dual-connection acceptance. The worker remains single-active-instance.

### Revised next priorities

1. **PostgreSQL correctness tests first:** prove concurrent lease claims, lease takeover, heartbeat rejection after takeover, stale child-write rejection, fenced terminal transitions, and atomic request-ledger/coverage advancement using two independent connections.
2. **Shared provider security boundary:** DNS/IP pinning for the five current official clients is now implemented at source level through a shared handler factory. Next extend the boundary with redirect-by-redirect validation if redirects are ever enabled, per-tenant/provider concurrency and retry budgets, and executable DNS/socket tests. Keep fixed endpoint allowlists in every adapter.
3. **Provider catalog metadata:** expose provider code, source class, geographic/time coverage, required configuration, enabled state, result limitations, terms/policy URL, field authority and cost/quota class. Never expose secrets.
4. **Observation reconciliation and cache:** add durable links from repeated source observations to canonical Candidates; introduce a shared response cache only with tenant-safe cache keys, retention limits and privacy review.
5. **Remaining business workflows:** Schedule/Scheduler, deep-mining/Super Search, general website discovery and optional paid contact enrichment, each behind explicit opt-in and human review.
6. **Runtime acceptance:** Windows-native .NET 10 restore/build/test, PostgreSQL migrations and concurrency, Redis worker restart/recovery, live official-source contract checks and full candidate-to-human-approved-Lead workflow.

The earlier statement that the current provider contract is only a flat raw record is historical: the present contract includes source URL, publication/retrieval timestamps, payload hash, evidence classification and provider-result completeness. Further pagination, typed retry/quota metadata and a shared execution gateway remain future work.


### DNS/IP transport update (2026-11-05)

The five official provider/identity clients now use `CrmProspectingHttpHandlerFactory`: approved hostname allowlists, no system proxy, no automatic redirects, DNS answer validation and direct socket connection to the validated address. This prevents a validation-to-connect second DNS lookup for these clients. Unit-test source has been added for public-unicast classification and DI wiring, but has not been executed. PostgreSQL two-connection tests, runtime DNS/socket verification, full .NET build/test and Windows acceptance remain open.
