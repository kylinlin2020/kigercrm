# Prospecting provider operations

## Official USAspending adapter

The CRM module includes an opt-in adapter for the United States federal award search API.

- Provider code: `usaspending_awards`
- Fixed endpoint: `https://api.usaspending.gov/api/v2/search/spending_by_award/`
- Authentication: none (public API)
- Coverage: US federal award records only; it is not a general company directory.
- Current behavior: one page, up to 20 awards per Run; the adapter emits a warning when another page exists.
- Evidence: official award URL, retrieval timestamp, payload SHA-256, and `official_procurement` evidence type.
- Safety: HTTPS fixed host/path, redirects disabled in the typed HttpClient, 25-second client timeout, 6 MiB response cap, and no response-body echo in HTTP error messages.

Enable it only in environments where outbound access to the official endpoint is allowed:

```json
{
  "XiHan": {
    "CRM": {
      "Prospecting": {
        "EnableUsaSpendingProvider": true
      }
    }
  }
}
```

The provider is disabled when the setting is absent, false, or malformed. Add the provider code to an approved strategy's provider plan only after enabling it.

The synthetic `fake` adapter has a separate setting, `XiHan:CRM:Prospecting:EnableFakeProvider`, and should remain disabled outside isolated development/test environments.

## Data handling and review

- Award recipient names and procurement descriptions are source facts; they are not proof that the recipient is a suitable buyer.
- The adapter does not infer a contact, email address, phone number, or website.
- Candidate creation and duplicate screening remain downstream worker responsibilities.
- No adapter may create a CRM Lead directly. A human must approve a Candidate before conversion to the existing CRM Lead workflow.
- Do not use this source to automate outreach. All outbound messages remain subject to explicit human approval.

## Current limitations

- Only the first page is read. The warning must not be interpreted as exhaustive search.
- The adapter does not yet persist per-request usage/cost ledger rows or cache provider responses.
- Network access, API availability, legal/retention terms, and Windows/PostgreSQL end-to-end behavior have not yet been verified in the target runtime.
- Unit and source-wiring tests are checked in but have not been compiled or executed in the current development environment.
