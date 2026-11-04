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
