# CRM conversion UnitOfWork and PostgreSQL application-test gate

> Status: implementation/acceptance specification. This document is not evidence that the tests have run.

## Confirmed UnitOfWork API contract

The XiHan Framework UoW package documentation lists these `UnitOfWorkAttribute` constructor overloads:

- `UnitOfWorkAttribute()`
- `UnitOfWorkAttribute(bool isTransactional)`
- `UnitOfWorkAttribute(bool isTransactional, IsolationLevel isolationLevel)`
- `UnitOfWorkAttribute(bool isTransactional, IsolationLevel isolationLevel, int timeout)`

It also documents `IsTransactional` and `IsolationLevel` as explicit options. Therefore the two CRM conversion declarations using:

```csharp
[UnitOfWork(true, System.Data.IsolationLevel.ReadCommitted)]
```

match a documented overload and explicitly request a transactional ReadCommitted unit of work.

Reference: https://framework.docs.xihanfun.com/packages/uow

**Version caveat:** the documentation confirms the framework API shape, but this repository session has not restored the exact NuGet package graph for version 5.9.5 or compiled the solution. The exact package binary/API compatibility remains a build gate, not a completed verification.

## Application-level PostgreSQL tests still required

The manual two-session SQL procedure in `CRM-CONVERSION-CONCURRENCY-POSTGRES.md` validates PostgreSQL conditional-update behavior only. It does not prove that the application service, DI interception, repositories, and SqlSugar transaction enlistment share the same runtime unit of work.

Add provider-backed tests that execute the real application service through the same DI registration/proxy path used by the API. Use PostgreSQL and two independent service scopes/connections.

### Lead conversion race

1. Seed one tenant-scoped Lead in an allowed source status (New, Contacted, or Qualified).
2. Start two independent application-service calls for the same Lead behind a barrier so both requests overlap.
3. Assert exactly one Customer is created for the Lead and, when supplied, exactly one Contact is created.
4. Assert the Lead is Converted and its ConvertedCustomerId / ConvertedContactId reference the winning records.
5. Assert the losing request resolves to the already-converted result, or the documented conflict response if the runtime transaction semantics do not permit winner reload in that call; it must never create a second Customer/Contact.
6. Repeat with one caller failing after the claim and before final Lead update. Assert the transaction rolls back the claim and all inserted records, and a subsequent retry can convert successfully.

### Prospecting Candidate conversion race

1. Seed one Approved Candidate and its Campaign/Run under a tenant.
2. Start two independent application-service calls for the same Candidate.
3. Assert exactly one Lead exists for the Candidate source identity.
4. Assert Candidate is Converted and ConvertedLeadId points to that Lead.
5. Assert the losing request returns the same winning Lead after reloading the committed Candidate, or a documented conflict; never create a duplicate Lead.
6. Inject a failure after claim/Lead creation but before Candidate final update. Assert Candidate returns to Approved after rollback, the inserted Lead is absent, and retry succeeds.

### Transaction enlistment and isolation assertions

- Resolve each request through a distinct DI scope and verify the intercepted service method is invoked (not a manually constructed service instance).
- Verify the claim update, related entity inserts, and final status/link update use the same transactional SqlSugar client/connection for each request.
- Verify an exception at each post-claim write boundary rolls back all earlier writes.
- Verify the two-request race under PostgreSQL READ COMMITTED, including the wait/reload behavior after the competing transaction commits.
- Include tenant isolation: a matching identifier in another tenant must not be claimed, read, or returned.
- Capture the actual database provider and isolation level in the test fixture; do not infer either from the attribute text alone.

## CI and release gate

The backend CI workflow now provisions PostgreSQL 17 and supplies `KIGER_TEST_POSTGRES_CONNECTION_STRING` to the repository-level integration tests. The workflow is configured to run solution restore/build/test, but repository inspection currently shows no GitHub Actions runs for branch `kiger-dev`; therefore no CI execution result has been observed. Until a run completes successfully:

- keep the existing source-contract tests classified as static checks;
- keep the manual SQL acceptance procedure classified as database-semantic/manual verification;
- classify the application-level race and rollback tests as outstanding;
- do not claim PostgreSQL concurrency safety is proven;
- do not merge the draft PR before full restore/build/test, PostgreSQL integration tests, frontend checks, and Windows pull/build/runtime acceptance pass.

## Implemented PostgreSQL repository-level CI coverage

`backend/test/XiHan.BasicApp.Api.Tests/CrmConversionClaimPostgresIntegrationTests.cs` now runs two independent SqlSugar clients against the same PostgreSQL database. It covers four repository-level cases: Lead claim race, Candidate claim race, cross-tenant Lead claim/read denial, and cross-tenant Candidate claim/read denial. Each race asserts exactly one conditional claim succeeds and the persisted status is `Converting`; each cross-tenant case asserts the other tenant cannot read or claim the record and the owner tenant's original status remains unchanged.

The backend CI job provisions PostgreSQL 17 and supplies `KIGER_TEST_POSTGRES_CONNECTION_STRING`; the tests are conditionally skipped when that variable is absent in a local environment.

**Scope boundary:** these are real PostgreSQL repository-level conditional-update tests. They do not yet invoke `CrmLeadAppService.ConvertAsync` or `CrmProspectingAppService.ConvertApprovedCandidateToLeadAsync` through DI interception, and do not prove that claim + related inserts + final status update share one application UoW. The application-level tests in the preceding section remain required. The CI run must pass before this repository-level coverage is considered verified.
