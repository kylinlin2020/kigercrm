# KIGER CRM Core — Implementation Baseline

**Status:** Phase 0 baseline on `kiger-dev`  
**Repository:** `kylinlin2020/kigercrm`  
**Base:** XiHan.BasicApp v5.9.1 (`34f127757f588ff2139875c4c5fd491ccc4e423f`)

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
