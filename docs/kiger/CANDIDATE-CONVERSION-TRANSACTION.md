# Prospecting candidate to Lead transaction and retry behavior

The conversion application method is marked [UnitOfWork(true)]. In the XiHan Framework 4.8.0 source, the unit-of-work interceptor wraps the application method, commits only after successful completion, and the SqlSugar client resolver pins one concrete connection per ConfigId for the lifetime of the transaction. The candidate and Lead entities use the tenant's default CRM data source.

The workflow is:

1. Load candidate and authorize access through its campaign.
2. If the candidate is already Converted, validate its ConvertedLeadId, load that existing Lead, and return its detail DTO. This makes a client retry after a successful commit idempotent.
3. Otherwise require Approved and conditionally claim Approved -> Converting using one SQL update.
4. Create the Lead and update the candidate to Converted with the Lead ID.
5. Commit all writes together when the application method returns successfully.

If Lead creation or the final candidate update fails, the transaction rolls back the Converting claim and any inserted Lead. A competing request cannot also claim the same Approved candidate. If it observed an old Approved snapshot before the winning transaction committed, its conditional claim returns false; a subsequent client retry reads Converted and returns the existing Lead.

The Converting state is an in-transaction claim, not a durable lease. It should not remain persisted after a failed request because the claim, Lead insert, and final candidate update share the same transaction. This depends on the application method being invoked through the framework interceptor; direct self-invocation or bypassing the application service is outside this guarantee.

## Verification still required

No compiler or database test has been run in this environment. PostgreSQL tests must use independent connections and cover simultaneous conversion attempts, failure after claim, failure after Lead insert but before candidate update, retry after commit, and tenant isolation. Windows pull/build/runtime acceptance remains outstanding.
