# Lead duplicate candidate API

Implemented on `kiger-dev`:

- Added `CrmLeadDuplicateCheckDto` and `CrmLeadDuplicateCandidateDto`.
- Added a read-permission-protected Dynamic API endpoint, `FindPotentialDuplicatesAsync`.
- Candidate queries run through the tenant-scoped CRM Lead repository, add the platform owner/assignee data-scope filter, and invoke the CRM field-security query guard for each signal query.
- Each signal query is capped at 100 rows; the union is scored by the deterministic duplicate matcher and the API returns at most 50 ranked candidates.
- The source lead ID can be excluded, cancellation is propagated, inputs are length-validated, and empty identity input is rejected.
- Company-name matching remains a suggestion only; no create operation is blocked and no uniqueness constraint is introduced.

Known preselection limitation (important):
- Candidate retrieval is a bounded prefilter, not a full-table fuzzy scan. Email is queried after normalization; company name uses the platform keyword search; phone and website currently use exact stored-value preselection. The scoring engine can normalize phone formatting and website hosts only after a row has been selected, so formatting/path variants may not be retrieved in this first API increment. A follow-up should add provider-safe indexed normalization/search or dedicated normalized columns with a migration before claiming exhaustive duplicate detection.
- The per-signal 100-row cap means very large collision groups are intentionally bounded; the UI must present this as a candidate warning, not as proof that no duplicate exists when the result is empty.

Validation:
- Source committed to `kiger-dev`.
- No .NET restore/build/test or database execution has been performed. The Windows validation gate remains open.
