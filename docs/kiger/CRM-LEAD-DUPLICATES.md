# Lead duplicate candidate API

Implemented on `kiger-dev`:

- Added `CrmLeadDuplicateCheckDto` and `CrmLeadDuplicateCandidateDto`.
- Added a read-permission-protected Dynamic API endpoint, `FindPotentialDuplicatesAsync`.
- Candidate queries run through the tenant-scoped CRM Lead repository, add the platform owner/assignee data-scope filter, and invoke the CRM field-security query guard for each signal query.
- Each signal query is capped at 100 rows; the union is scored by the deterministic duplicate matcher and the API returns at most 50 ranked candidates.
- The source lead ID can be excluded, cancellation is propagated, inputs are length-validated, and empty identity input is rejected.
- Company-name matching remains a suggestion only; no create operation is blocked and no uniqueness constraint is introduced.

## Normalized candidate lookup (5.9.2)

Phone and website candidate preselection now use persisted normalized keys instead of exact raw-value equality:

- `duplicate_email_normalized`: trimmed, lowercase email with a trailing domain dot removed.
- `phone_digits`: ASCII digits only; country-code digits are preserved.
- `website_host`: lowercase host, without scheme, path, query, `www.`, or a numeric port.
- The Lead domain service refreshes all three keys on create and update.
- PostgreSQL migration `5.9.2/5.9.2.sql` adds nullable columns, backfills existing CRM Lead rows, and adds tenant-prefixed lookup indexes.
- These are ordinary non-unique indexes. No email, phone, company, or website uniqueness constraint is added.

## Remaining limits

- Candidate retrieval remains bounded to 100 rows per signal. Large collision groups can be truncated; an empty result is not proof that no duplicate exists.
- Company-name lookup still uses the platform keyword search and remains a weaker signal.
- The website host backfill handles common DNS hostnames and numeric ports. Internationalized hostnames and unusual URL authorities should be covered by integration tests before claiming complete equivalence with .NET URI normalization.
- Normalized keys are maintained through the Lead domain service. Any future bulk import or direct repository write path must use the same normalization helper or update the keys explicitly.
- The API is advisory only; it does not block Lead creation or conversion.

## Validation status

- Source and PostgreSQL migration committed to `kiger-dev`.
- Added a SQLite integration test that exercises the real Lead domain create path and verifies normalized values are persisted and queryable. The test has been committed but not executed.
- No .NET restore/build/test or PostgreSQL migration execution has been performed. The Windows validation gate remains open.
