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

## Phone-key backfill alignment (5.9.24)

- Migration `5.9.24` recomputes historical `phone_digits` using the same conservative intent as the application normalizer: strip recognized extension suffixes, then keep only 7..15 digit values.
- This is a data correction for databases that already applied `5.9.2`; it does not add uniqueness constraints or change the original phone field.
- PostgreSQL execution is still required before treating the migration as production-verified.

## Historical email-key cleanup (5.9.25)

- Migration `5.9.25` clears normalized email keys for blank values, whitespace-containing values, values without exactly one `@`, and domains with leading/trailing or repeated dots.
- Original email values remain unchanged. The migration is intentionally conservative and does not claim full equivalence with .NET Unicode normalization; the application matcher remains authoritative for scoring.
- PostgreSQL execution and locale/Unicode behavior still require provider-specific validation.

## Historical website-key cleanup (5.9.26)

- Migration `5.9.26` rebuilds `website_host` from the original website value rather than trusting the legacy regex-derived host.
- Only HTTP(S) or scheme-less, ASCII DNS hostnames with valid label shape are indexed. Userinfo, IP literals, localhost/single-label hosts, unsupported schemes, malformed authorities, and non-ASCII/IDN values are cleared to `NULL`.
- PostgreSQL core does not guarantee IDNA conversion. Non-ASCII domains are intentionally omitted from historical duplicate lookup until the Lead is edited through the application normalizer, which uses `Uri.IdnHost`.
- This is a conservative false-negative tradeoff to prevent malformed or non-equivalent historical keys from creating misleading duplicate matches. PostgreSQL execution remains a required deployment validation step.

## Website-key port validation follow-up (5.9.27)

- Migration `5.9.27` re-runs the conservative historical website-key backfill without accepting any explicit port.
- This intentionally clears even valid port-bearing URLs from historical duplicate lookup. It avoids indexing hosts from malformed or out-of-range ports that `.NET System.Uri` would reject.
- The original URL remains intact; editing the Lead through the application can repopulate a normalized host when the URL is valid.
- PostgreSQL execution is still required before treating this migration as operationally verified.

## Lead conversion concurrency guard (5.9.28)

- Customer rows created by Lead conversion now retain `ConvertedLeadId`.
- A tenant-scoped unique index on `(TenantId, ConvertedLeadId)` prevents two concurrent conversion transactions from committing two Customer rows for the same Lead. Multiple ordinary Customers remain valid because the column is nullable; PostgreSQL and SQLite unique indexes allow multiple NULL values.
- The unique constraint is a final database invariant, not a promise that simultaneous requests both return success: the losing transaction may receive a uniqueness error. A subsequent retry observes the already-converted Lead and returns its existing result.
- The transaction still contains Customer, optional Contact, and Lead state update. A PostgreSQL two-connection concurrency test and Windows runtime validation remain required.

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
