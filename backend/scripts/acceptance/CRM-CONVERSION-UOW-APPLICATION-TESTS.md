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

The Lead source-wiring regression test was also aligned with the explicit `[UnitOfWork(true, System.Data.IsolationLevel.ReadCommitted)]` declaration so it no longer searches for the superseded `[UnitOfWork(true)]` form.

The Candidate conversion source-wiring test now also guards the losing-claim path: it must reload the candidate after a failed conditional claim, return the committed Lead when the candidate is already Converted, and validate the referenced Lead's Prospecting source, source reference, and campaign owner.

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


The extraction persistence contract now checks that submission acknowledgement, lifecycle transition, cursor advancement, successful completion, and incomplete-result finalization all include the tenant predicate, exact lease token, fencing version, and unexpired database-time lease in their conditional updates. This is a source-level regression guard; PostgreSQL execution remains required.

## Quote-to-sales-order conversion isolation (2026-11-07)

- The quote conversion application-service UnitOfWork now explicitly requests `ReadCommitted` rather than relying on the framework/database default.
- The source-level wiring suite asserts the conversion endpoint retains this isolation attribute.
- Rationale: concurrent conversion requests must not run under a repeatable-read/snapshot isolation assumption when the loser reloads the committed winner on a subsequent request. The tenant-scoped unique source-quote index and quote optimistic update remain the database safety guards; a losing concurrent request may still fail and must be retried to receive the already-created order.
- This is a source-level contract only. It does not prove the exact deployed framework UoW provider honors the attribute, nor does it replace PostgreSQL two-connection conversion/rollback tests. Those tests and full restore/build/test remain pending.

## Quote optimistic-update repository test source (2026-11-07)

- Added `CrmQuoteOptimisticConcurrencySqliteIntegrationTests.UpdateAsync_ShouldRejectAStaleAcceptedQuoteConversionLink`.
- The test uses the real `CrmQuoteRepository` with an in-memory SQLite database, reads the same Accepted quote twice, persists one Converted/order-link winner, then asserts the stale second update raises the framework `ConcurrencyConflictException` and cannot overwrite the winning order link.
- This is a repository-level Row_Version stale-write test only. It does not execute `CrmQuoteAppService.ConvertToSalesOrderAsync`, prove UnitOfWork rollback of order header/lines, or exercise PostgreSQL isolation/unique-index behavior. Test source is committed but has not been compiled or executed.

## Quote-to-sales-order PostgreSQL application acceptance plan (2026-11-07)

This section is the concrete test contract for `CrmQuoteAppService.ConvertToSalesOrderAsync`. It is intentionally separate from the SQLite repository Row_Version test and from the Lead/Candidate repository claim tests above.

### Fixture and invocation requirements

- Use a disposable PostgreSQL database matching the CI PostgreSQL major version and the repository's actual migration/CodeFirst setup.
- Seed one tenant-scoped Accepted quote with at least two quote lines, a customer, and any referenced contact/opportunity required by the service.
- Resolve `CrmQuoteAppService` from the production API DI registration in two independent request scopes. Invoke through the same dynamic-proxy/interceptor path that applies `[UnitOfWork]`; do not instantiate the service directly.
- Ensure each scope uses a distinct SqlSugar connection/client while targeting the same database and tenant.
- Record provider, effective transaction isolation, connection identity, and transaction identity at each repository write. The expected isolation is PostgreSQL READ COMMITTED, but the attribute text alone is not proof.
- Use deterministic test-only fault injection at repository boundaries; do not add production-only failure switches.

### Concurrent first conversion

1. Start two service calls for the same Accepted quote behind a barrier.
2. Assert the database contains exactly one active sales-order header with that `SourceQuoteId`, exactly the expected copied order lines, and one order number `SO-{quote.BasicId}`.
3. Assert the quote is Converted and `ConvertedOrderId` equals the sole order header ID.
4. The losing request may surface the documented optimistic-concurrency/unique-constraint conflict. Retry through a fresh request scope and assert the retry returns the already-created order rather than inserting another header or lines.
5. Verify both the tenant-scoped source-quote unique index and tenant-scoped order-number unique index exist in PostgreSQL and reject conflicting inserts. Also verify identical source quote IDs in different tenants remain independent.
6. Assert no duplicate active source-quote order exists even when the losing request fails.

### Atomic rollback fault matrix

Run each case from a freshly seeded Accepted quote and assert all persisted state after the exception:

| Injected failure point | Required post-failure state |
| --- | --- |
| Immediately after sales-order header insert | No order header; quote remains Accepted with null ConvertedOrderId; no order lines |
| After the first copied order line insert | No order header; quote remains Accepted with null ConvertedOrderId; no order lines |
| After all order lines, before quote update | No order header; quote remains Accepted with null ConvertedOrderId; no order lines |
| During/after quote Row_Version update but before transaction commit | No order header; quote remains Accepted with null ConvertedOrderId; no order lines |

After each rollback case, remove the injected failure and retry conversion. Assert exactly one complete order is created and linked. This proves the claim/write/quote-link sequence shares one real transaction; a source-ordering assertion is not sufficient.

### Isolation and retry semantics

- Have request A read the Accepted quote, then allow request B to complete and commit before A attempts its final quote update. Verify A cannot overwrite B's link and that its transaction does not leave orphaned order rows.
- From a fresh request scope after B commits, verify the quote is read as Converted and the existing order is returned.
- Confirm the actual transaction isolation is READ COMMITTED for both service invocations. If the framework interceptor does not apply the requested isolation, fail the test rather than weakening the assertion.
- Capture PostgreSQL SQLSTATE for uniqueness/concurrency failures, but assert business invariants rather than coupling the test to one provider exception message.

### Current implementation boundary

The current `CrmQuoteOptimisticConcurrencySqliteIntegrationTests` only proves repository-level stale `Row_Version` writes are rejected. It does not prove service interception, PostgreSQL unique-index behavior, transaction enlistment, rollback, or the race above. All cases in this section remain **not implemented / not executed** until a provider-backed application test fixture exists and its CI run passes.

### Test-host wiring prerequisite and groundwork

The API test project has now been given a direct project reference to `XiHan.BasicApp.WebHost` plus `Microsoft.AspNetCore.App`. This makes the production root module and ASP.NET Core host assemblies available to future application-level integration tests. The WebHost already declares the CRM module in its real `[DependsOn]` graph.

This is only compile-time wiring groundwork. Existing PostgreSQL conversion tests still construct repositories with standalone SqlSugar clients; they do not bootstrap the API host or its dynamic-proxy UnitOfWork interception. A genuine quote application-service test still needs a supported host bootstrap that loads production WebHost service registrations and interception while allowing two independent request scopes to use separate PostgreSQL connections. Do not manually instantiate `CrmQuoteAppService` and treat that as equivalent.

The project reference change has not yet been restored or built, so dependency-graph compatibility remains unverified. This is not evidence that application-level tests have run.
