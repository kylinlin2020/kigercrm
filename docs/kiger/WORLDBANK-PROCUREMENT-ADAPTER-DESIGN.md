# World Bank procurement data: safe CRM integration design

## Decision

Do not register the World Bank Finances One Procurement Notice dataset as a direct `ICrmProspectingProvider` yet.

The official dataset is valuable for finding relevant tenders, countries, sectors, project IDs and procurement deadlines. However, its published 15-column schema exposes bid description, notice type, publication/deadline dates, project ID, procurement category/method, country, region, sector and notice URL; it does not expose a buyer organisation or buyer contact field. The CRM worker currently falls back from an empty CompanyName to Title. Passing a bid title as Title would therefore risk creating a procurement description as if it were a company.

## Verified official dataset

- Dataset: `DS00979` (Procurement Notice)
- Resource: `RS00909`
- API host/path: `https://datacatalogapi.worldbank.org/dexapps/fone/api/apiservice`
- Example base query: `?datasetId=DS00979&resourceId=RS00909&top=100&type=json`
- Pagination: `top` and `skip`; documented API limit is 1,000 records per request.
- Dataset is public, worldwide, updated daily, with historical notice coverage from September 2005.
- License shown by Finances One: Creative Commons Attribution 4.0 International.

## Required second-stage identity resolution

Before this dataset can create prospecting Candidates:

1. Ingest only bounded procurement notice metadata and preserve the official notice URL and raw-row SHA-256.
2. Extract the World Bank Project ID from the notice row.
3. Resolve the project through an official World Bank project API or project detail source.
4. Identify the borrower / implementing agency only when the source explicitly provides that role and a verifiable entity name.
5. Do not substitute the World Bank, project title, procurement description, supplier, or country for the implementing agency.
6. Store the project notice as evidence and keep the agency identity evidence separately attributable to the second source.
7. If the agency cannot be resolved unambiguously, retain it in research output only and do not create a Candidate.

## Proposed controls

- Provider remains disabled by default.
- At most 2 pages × 1,000 rows per Run until query filters and coverage cursors are tested against the live API.
- Use a rolling publication-date filter only after confirming exact API query parameter semantics; otherwise local filtering must clearly report the bounded page coverage and must not claim completeness.
- Fixed HTTPS host/path, redirects disabled, 20-second timeout, 6 MiB response cap, and no arbitrary URL following.
- Respect source attribution and CC BY 4.0 requirements in stored provenance and exported reports.
- Never automatically convert a research row into a Lead; human review remains mandatory.

## Official references

- Dataset and field schema: https://financesone.worldbank.org/procurement-notice/DS00979
- API Explorer and documented request cap/parameters: https://financesone.worldbank.org/api-explorer?id=DS00979
- World Bank Procurement Notices portal: https://projects.worldbank.org/en/projects-operations/procurement

## Acceptance criteria before implementation

- Capture a real sample response and verify exact JSON property names/types.
- Verify date filtering and sort/ordering behavior.
- Verify project detail lookup returns an explicit implementing agency for a sample of countries/projects.
- Add tests proving that missing/ambiguous implementing agency data produces no CRM Candidate.
- Add attribution metadata and a cross-source evidence chain.
