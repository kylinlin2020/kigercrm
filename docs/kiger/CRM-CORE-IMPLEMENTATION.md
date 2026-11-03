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
