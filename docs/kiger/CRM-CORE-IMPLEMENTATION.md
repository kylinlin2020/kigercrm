# KIGER CRM Core — Implementation Baseline

**Status:** Phase 0 baseline on `kiger-dev`  
**Repository:** `kylinlin2020/kigercrm`  
**Base:** XiHan.BasicApp v5.9.1 (`34f127757f588ff2139875c4c5fd491ccc4e423f`)

## Customer and contact query increment (2026-11-02)

Implemented:
- Customer and Contact repository contracts and tenant-scoped SqlSugar repository implementations, using the platform SaasRepository base.
- Customer and Contact query contracts, paginated query DTOs, list/detail projections and explicit mapper methods.
- Customer search by name/legal name/website, region, industry and owner.
- Contact search by name/email/phone, customer and owner.
- Both query services apply the platform field-security query guard, permission authorization and deterministic created-time default ordering.
- CRM field-security registration is deliberately staged to Customer, Contact and Lead only. Opportunity, Activity and Task entities remain modeled but are not yet activated in the field-security catalog until their guarded services are implemented.

Validation status:
- Source has been committed to `kiger-dev`.
- No successful .NET build/test result is available yet. The API field-security wiring test is expected to verify the currently activated three entities once CI is run.
- No create/update/delete endpoints or schema migrations are included in this increment.

## Field-security mapping increment (2026-11-02)

The CRM module now has explicit response DTOs and mapper methods for Customer, Contact, Opportunity, Activity and Task, in addition to Lead. This is required because BasicApp discovers field-security DTO mappings from public static mapper methods whose first parameter is a registered entity.

The API contract test project now references the CRM module and includes `AddCrmFieldSecurityEntities()` in its wiring fixture. The existing field-security wiring tests can therefore detect missing CRM DTO mappings and missing query guards.

This wiring has been authored but not executed in this environment. The .NET build/test result remains unverified until CI or a local .NET SDK run succeeds.

## Delivery status (2026-11-02)

The first implementation increment is committed on `kiger-dev` and proposed as draft PR #1.

Implemented in source:
- CRM module project and WebHost module dependency registration.
- Tenant-scoped persistence entities for Customer, Contact, Lead, Opportunity, Activity and Task.
- CRM field-security entity registration.
- Tenant-side CRM permission catalog seeder; menu registry/seeder authored but activation deferred until matching frontend routes exist.
- Lead query contract, DTOs, explicit mapper, tenant-scoped repository and paginated query service.
- WebHost module dependency regression test updated to include CRM.

Not yet implemented or verified:
- Customer/Contact/Lead command APIs (create, update, delete, conversion).
- Opportunity, Activity and Task application services.
- Database migration / schema upgrade scripts.
- Frontend pages and localization.
- End-to-end tenant-isolation, permission and field-security tests.
- Successful .NET restore/build/test. GitHub Actions currently reports no workflow runs for this draft branch; do not interpret absence of checks as a passing build.

The CRM menu registry currently declares intended component paths, but menu seeding is intentionally disabled until matching frontend routes/components exist.

## 1. Objective

Build KIGER CRM as a business module on top of XiHan.BasicApp. BasicApp remains the platform foundation; KIGER owns CRM domain behavior. Do not copy Haituo's Express/TypeScript service layer into the .NET application.

Initial business scope:
- Accounts / customers
- Contacts
- Leads
- Opportunities
- Activities and follow-ups
- Tasks and reminders

Out of the first increment: autonomous AI actions, bulk lead scraping, WhatsApp session hosting, deep email synchronization, and complex scoring models.

## 2. Architecture boundaries

| Area | Responsibility |
|---|---|
| BasicApp platform | Authentication, tenant context, users, RBAC, data scope, audit, files, notifications, workflow, AI provider abstractions |
| KIGER CRM Core | Customer, contact, lead, opportunity, activity, task domain entities and application services |
| Integration adapters | Go CRM REST API, email, WhatsApp Business API, external lead providers |
| AI Sales Agent (later phase) | Read CRM context, propose plans and actions; execution requires explicit approval and audit |

Integration adapters must not become dependencies of the CRM domain. The Go CRM REST API will be implemented as an isolated connector using the supplied OpenAPI contract, not embedded into domain entities.

## 3. Tenant and ownership rules

- Every CRM record must be scoped to the authenticated BasicApp tenant.
- Record ownership must use the BasicApp user identity; never trust a client-supplied tenant or owner identifier.
- Team membership and data visibility must be enforced through the platform's data-scope mechanism.
- Preserve source-system identifiers during imports for idempotency and traceability.
- Define explicit mappings for Haituo `team_id`, `owner_id`, and historical records before migration.
- Do not expose cross-tenant lookup endpoints.

## 4. Initial domain model

### Customer (Account)
- Id
- TenantId (platform tenant scope)
- Name
- LegalName (optional)
- Website (optional)
- Country/Region (optional)
- Industry (optional)
- Source (optional)
- OwnerUserId
- Status
- Notes (optional)
- ExternalSystem / ExternalId (optional, unique within tenant and source)

### Contact
- Id, CustomerId, TenantId
- FirstName, LastName, JobTitle
- Email, Phone, WhatsApp (optional)
- OwnerUserId
- IsPrimary
- Notes (optional)

### Lead
- Id, TenantId
- CompanyName, ContactName
- Email, Phone, Website, Country/Region
- Source, SourceReference
- Status (New, Qualified, Disqualified, Converted)
- Rating (rule-based initially)
- OwnerUserId
- ConvertedCustomerId (optional)
- ConvertedContactId (optional)
- LastContactedAt (optional)

### Opportunity
- Id, TenantId, CustomerId, PrimaryContactId (optional)
- Name, Stage, Amount, Currency
- ExpectedCloseDate (optional)
- Probability (optional)
- OwnerUserId
- LostReason (optional)

### Activity
- Id, TenantId
- Related entity type and id
- Activity type (Call, Email, Meeting, Note, WhatsApp, Other)
- Subject, Content (optional)
- OccurredAt
- OwnerUserId
- ExternalSystem / ExternalId (optional)

### Task
- Id, TenantId
- Related entity type and id (optional)
- Title, Description (optional)
- DueAt (optional)
- Status, Priority
- AssigneeUserId, CreatedByUserId
- CompletedAt (optional)

The model is a domain starting point, not a finalized database schema. Exact base classes, audit fields, soft-delete behavior, aggregate boundaries, and persistence mappings must follow the checked-in BasicApp conventions.

## 5. API behavior

First increment should expose authenticated, tenant-scoped CRUD and search operations for customers, contacts, leads, opportunities, activities, and tasks.

Required behaviors:
- Pagination and stable sorting for list endpoints.
- Server-side validation and normalized email/phone values.
- Duplicate checks for customer and lead imports.
- Lead conversion must be transactional and idempotent.
- Every write must record actor and audit metadata using platform facilities.
- Do not allow clients to set TenantId, audit fields, or privileged ownership fields directly.
- Use platform permission catalog and menu contribution conventions; do not hard-code authorization only in controllers.

## 6. Delivery sequence

1. Verify module registration, entity, repository, migration, permission, menu, and test conventions against the exact v5.9.1 source.
2. Add the KIGER CRM module skeleton and register it through the supported module dependency mechanism.
3. Implement Customer and Contact first, with tenant isolation and permission tests.
4. Implement Lead and transactional Lead Conversion.
5. Implement Opportunity, Activity, and Task.
6. Add frontend pages following the existing Vue/TypeScript conventions.
7. Add Go CRM connector after the OpenAPI document is checked into a secure, non-secret configuration location.
8. Add integration and migration tooling with idempotent import keys.

## 7. Quality gates

- Build the affected backend projects.
- Run unit and integration tests for tenant isolation, permissions, validation, and lead conversion.
- Verify database migration on a clean development database and an upgrade database.
- Verify list queries are paginated and tenant-filtered.
- No secrets, access tokens, customer exports, or WhatsApp credentials in source control.
- No change to `main` until a reviewed pull request is approved.

## 8. Open decisions

- Confirm target database provider and migration strategy before generating provider-specific migrations. PostgreSQL is a candidate, not yet locked.
- Confirm the BasicApp version's exact business-module template and frontend menu/page registration APIs before creating implementation files.
- Confirm Go CRM OpenAPI version and authentication scheme before implementing the connector.
- Confirm whether CRM customer identity is unique by normalized domain, legal name, or an operator-approved combination; do not impose an unsafe global uniqueness rule.

## 9. Source migration constraints

Haituo CRM is an Express/TypeScript application with MySQL-oriented persistence and team/owner semantics. Port business rules and contracts selectively. Do not copy its persistence implementation directly. WhatsApp transport and GPL-sensitive dependencies remain isolated pending license review; prefer evaluating the official Meta WhatsApp Business API for production integration.


## Customer command workflow increment (2026-11-03)

Implemented on `kiger-dev`:
- Customer create/update DTOs and application contracts.
- Customer domain command interface and implementation with normalized required name, bounded optional field lengths, positive owner-id validation, and entity existence checks.
- Customer application command service with Create/Update/Delete permission attributes, unit-of-work boundaries, and field-security create/update guards.
- Explicit command mapping from API DTOs into domain commands.
- Customer create/update responses reuse the established detail projection.

Important validation limits:
- This increment has not been compiled or executed against a database; no successful build/test is claimed.
- Tenant scoping relies on the platform `SaasRepository` and authenticated tenant context. This must be proven by integration tests.
- Contact CRUD, database migrations, frontend pages, Go CRM integration and lead conversion remain pending.
- Owner assignment authorization and customer/contact relationship policies still require explicit tests and, if needed, server-side policy enforcement before production.


## Contact command workflow increment (2026-11-03)

Implemented on `kiger-dev`:
- Contact create/update DTOs, application contract, command mapper and application service.
- Contact domain service for create/update/delete.
- Create/update operations enforce CRM permissions, unit-of-work boundaries and field-security guards.
- Contact writes validate required first/last names, normalize email casing, enforce field length limits and validate positive owner IDs.
- Contact create/update verifies that the referenced customer can be resolved through the current tenant-scoped customer repository before saving.

Validation and policy gaps:
- No successful .NET build/test or database integration test is claimed.
- Primary-contact uniqueness per customer is not yet enforced; it needs a transactional rule and concurrency-safe test before production.
- Owner assignment authorization, data-scope behavior and soft-delete semantics still require integration tests.
- Lead commands/conversion, migrations, frontend and Go CRM integration remain pending.


## Lead command workflow increment (2026-11-03)

This section supersedes earlier delivery-status bullets that described Customer/Contact/Lead command APIs as pending.

Implemented on `kiger-dev`:
- Lead create/update DTOs, application contract, command mapper and application service.
- Lead domain command interface and implementation for create/update/delete.
- Create/update enforce CRM permissions, unit-of-work boundaries and field-security guards.
- Lead creation initializes status to `New` and score to `0`; score remains system-managed rather than client-controlled.
- Lead update validates lifecycle values and does not permit clients to set `Converted`; converted leads cannot be edited or deleted through generic CRUD.
- Email is trimmed and normalized to lowercase; optional field lengths and positive owner IDs are validated.

Remaining limitations:
- Lead conversion into Customer/Contact is not implemented yet. The generic CRUD path deliberately cannot mark a lead converted.
- Duplicate detection, owner assignment authorization and data-scope policy still need explicit enforcement and integration tests.
- No successful .NET restore/build/test or database execution is claimed. As requested, final pull/build/test validation remains for the user's actual machine.


## Lead conversion and Opportunity command increment (2026-11-03)

Implemented on `kiger-dev`:
- Added a dedicated Lead `ConvertAsync` operation, separate from generic lead updates. It creates a tenant-scoped Customer and, when a contact name is present, a related Contact; it stores both generated IDs and marks the lead Converted in the same application unit-of-work.
- A repeated conversion request for an already-converted lead returns its existing linked records after verifying those references remain resolvable in the current tenant. Disqualified leads cannot be converted.
- Added Opportunity create/update/delete DTOs, contracts, mapper, tenant-scoped repository and domain/application services.
- Opportunity writes validate customer and optional primary-contact references within the current tenant, and require the primary contact to belong to the selected customer.
- Opportunity amount cannot be negative; currency is normalized to a three-letter uppercase code; probability is constrained to 0–100; Lost stage requires a reason.
- Opportunity entity is now registered in the field-security entity catalog because guarded write operations and explicit response mapping exist.

Validation and concurrency caveats:
- These changes have not been compiled or executed against a database. The user has explicitly reserved real-machine pull/build/test for the final validation stage.
- Lead conversion is protected by the application Unit of Work, but a simultaneous double-submit race has not yet been proven safe. A database-level concurrency/idempotency guard and integration test are still required before production.
- Contact creation during conversion is currently conditional on a non-empty ContactName; lead email/phone alone do not create a contact.
- Opportunity CRUD is implemented, but list/detail query APIs and full tenant/data-scope/owner authorization tests remain outstanding.
- Activity and Task application services, migrations, frontend, Go CRM connector and AI sales workflows remain outstanding.


## Opportunity pipeline query increment (2026-11-03)

Implemented on `kiger-dev`:
- Added a paginated Opportunity query contract and query DTO with Customer, Stage, Owner and keyword filters.
- Added list/detail response projections and a Dynamic API query service.
- The list query applies CRM Read permission, tenant-scoped repository access, field-security query guarding, validation of supplied filter identifiers/stages, and deterministic CreatedTime descending ordering.
- Detail queries reuse the explicit Opportunity response mapper.

Validation status:
- Source changes are committed; no .NET build, test, or database execution has been performed or claimed.
- Opportunity field-level masking on detail responses and owner/data-scope enforcement need explicit integration coverage.
- Activity and Task application workflows, migrations, frontend, and Go CRM connector remain pending.


## Activity and Task command workflows (2026-11-03)

Implemented on `kiger-dev`:
- Activity: repository, create/update DTOs, command contract/mapper, domain service and permission-protected application service for Call, Email, Meeting, Note, WhatsApp and Other records.
- Task: repository, create/update DTOs, command contract/mapper, domain service and permission-protected application service for task creation, editing, status changes and deletion.
- Task status changes maintain `CompletedAt`: set when entering Completed and clear when leaving Completed.
- Both Activity and Task now have explicit field-security entity registration alongside Customer, Contact, Lead and Opportunity.
- Task validation covers related-reference pair consistency, title length, status enum, priority range 1–5 and positive assignee IDs. Activity validation covers related type/id shape, activity enum, subject length and optional external identifier lengths.

Important remaining work / validation:
- Activity and Task paginated list/detail query APIs are not yet implemented.
- Activity related-record IDs and Task optional related-record IDs are shape-validated but are not yet resolved against the corresponding tenant-scoped Customer/Contact/Lead/Opportunity repositories. This must be added before production.
- Owner/assignee authorization, data-scope enforcement, field-level masking on returned DTOs, and soft-delete behavior need integration tests.
- No .NET build/test or database execution is claimed.
- Go CRM REST Connector is explicitly out of scope: KIGER is the integrated CRM product; no separate Go CRM connector will be built.


## Activity / Task query and relation validation increment (2026-11-03)

Implemented on `kiger-dev`:
- Activity page/detail query API with related-record, activity-type, owner, occurrence-window and keyword filters; read permission, tenant repository scope, field-security query guard and occurrence-time ordering.
- Task page/detail query API with related-record, status, priority, assignee, due-window, overdue-only and keyword filters; read permission, tenant repository scope, field-security query guard and due-time ordering.
- Activity create/update now resolves the related record against tenant-scoped Customer, Contact, Lead or Opportunity repositories; unsupported types and missing records are rejected.
- Task create/update now validates optional linked records against the same tenant-scoped CRM repositories; a type/id pair must be supplied together.

Project scope clarification:
- Go CRM REST Connector is excluded. KIGER is the CRM product and the internal CRM module is the system of record.

Remaining:
- Detail DTO field-level masking needs an explicit supported response-guard implementation and tests; list query guards are present.
- Owner/assignee assignment authorization and row-level data-scope policies still need dedicated enforcement and tests.
- Verify SqlSugar date comparison overloads and repository/DTO API signatures during the final machine build; no build or test pass is claimed.
- Database migration generation, integration tests, duplicate detection and frontend workflows remain in progress.


## Task status field-security and tenant-index regression guard (2026-11-03)

- Task status changes now accept a dedicated `CrmTaskStatusUpdateDto` rather than primitive route arguments.
- The application service invokes `EnsureUpdatableAsync(typeof(SysCrmTask), input.Id, input, ...)` before the domain transition, so the Status field is subject to field-security write policy.
- The API test suite's cross-module unique-index guard now includes the CRM module. CRM tenant-scoped unique indexes are therefore covered by the same TenantId / IsDeleted regression checks as other platform modules.
- The existing Customer unique index is tenant-scoped and includes IsDeleted. Other CRM indexes inspected in this increment are non-unique lookup indexes; primary-contact uniqueness still needs a database-safe filtered-unique strategy rather than an unsafe composite index that would also constrain non-primary contacts.
- No test execution or build success is claimed; these regression checks are added for the final real-machine validation.


## CRM security regression-test coverage (2026-11-03)

- Corrected the field-security wiring test to recognize both `fieldSecurity` and `_fieldSecurity` service-field naming conventions. CRM application/query services use the former; without this, the test could miss CRM query guards and write guards.
- Added the CRM module assembly to the entity-placement test inventory so CRM entities are checked for strict tenant-database isolation alongside the other business modules.
- These are test-suite changes only; they have not been executed in this environment. The final Windows pull/build/test remains required.

## CRM user data-scope enforcement (2026-11-03)

Implemented on `kiger-dev`:
- Added a shared CRM query extension that resolves the platform `IUserDataScopeFilterService` and applies an `IN` predicate through the typed `QueryConditions.AddFilterIn` API.
- Customer, Contact, Lead, Opportunity and Activity list queries are restricted by `OwnerUserId`.
- Task list queries are restricted by `AssigneeUserId`.
- All six detail query methods re-check the resolved user scope before returning a DTO; records outside scope are returned as not found (`null`).
- Unrestricted platform/all-data contexts retain full visibility. For restricted contexts, unassigned records are excluded deliberately; an empty accessible-user set yields an always-nonmatching sentinel filter rather than silently disabling the scope.
- User-provided filters are combined with the mandatory data-scope predicate, so specifying another owner cannot broaden visibility.

Important validation boundary:
- This increment covers query-time visibility only. Create/update assignment authorization (whether a caller may assign an owner or assignee outside their allowed user set) remains a separate command-side task and must be completed before production release.
- No successful .NET restore/build/test has been observed yet. The change is committed to the draft branch but remains unverified by runtime tests.


## Command-side owner and assignee data-scope enforcement (2026-11-03)

Implemented on `kiger-dev`:
- Added a shared application-layer assignment authorization guard using BasicApp's `IUserDataScopeFilterService.ResolveAccessibleUsersAsync`.
- Customer, Contact, Lead, Opportunity and Activity create/update operations now reject a requested `OwnerUserId` outside the caller's resolved user data scope.
- Task create/update operations now reject a requested `AssigneeUserId` outside the caller's resolved user data scope.
- Null owner/assignee remains allowed; it does not grant visibility because query-time data-scope filtering continues to exclude unassigned records for restricted scopes.
- The authorization check runs before domain writes and is independent of field-security write guards.

Validation boundary:
- Source-level wiring has been re-read from the branch after commit. No .NET restore/build/test or runtime authorization test has been executed; the final Windows validation gate remains open.
- The platform's data-scope resolver is trusted as the authority for allowed user IDs. Integration tests must verify restricted, unrestricted, empty-scope, and changed-team-membership cases.
- Lead duplicate recognition and database-safe conversion concurrency protection remain open; no provider-specific uniqueness or locking strategy is being guessed before the actual database provider and migration conventions are validated.


## CRM command-side assignment guard correction and regression test (2026-11-03)

- Corrected Lead create/update assignment authorization: Create now performs exactly one owner-scope check; Update now performs the same check before field-security validation and domain mutation.
- Added a source-wiring regression test covering Customer, Contact, Lead, Opportunity, Activity and Task create/update methods. It asserts exactly one owner/assignee scope check per write endpoint, catching both missing and duplicated checks.
- This is a static source-wiring test; it has been committed but not executed. Runtime authorization and compilation remain unverified until CI or the requested Windows machine validation.


## Full CRM assignment guard audit correction (2026-11-03)

A follow-up audit across all six application services found the same insertion defect in Customer, Contact, Opportunity, Activity and Task that had first been corrected in Lead: the create method had two identical scope checks and the update method had none. All five services are now corrected so create and update each perform exactly one check. Lead was already corrected in the preceding commit.

The API test project's source-wiring regression now checks both write methods for all six services and excludes Task status-only transitions, which do not change the assignee. This test has not yet been executed; source review confirms the expected guard placement, but compilation and runtime authorization remain open.


## Existing-record data-scope enforcement on CRM mutations (2026-11-03)

A security audit found that validating only the requested new owner/assignee did not prevent a caller from addressing another user's existing CRM record by ID. The application layer now loads the current tenant-scoped record and checks its current owner/assignee against the caller's resolved user data scope before mutating it:

- Customer, Contact, Opportunity and Activity: Update and Delete.
- Lead: Update, Convert and Delete.
- Task: Update, ChangeStatus and Delete.

The shared `CrmAssignmentAuthorization.EnsureRecordAccessibleAsync` denies records with no owner/assignee to restricted scopes, matching the existing query-side `CanReadOwnedRecord` rule. Unrestricted scopes remain unrestricted. Target owner/assignee validation remains separate, so the old and new ownership checks are both applied on reassignment.

A source-wiring regression test now covers every listed mutation endpoint. It has not been executed. This is application-layer protection; repository-level tests and actual database/provider validation remain outstanding.
