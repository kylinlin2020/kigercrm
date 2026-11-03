# CRM customer primary-contact invariant

## Data model

A customer may have many contacts, but at most one contact may be marked primary.

- `SysCrmContact.IsPrimary` remains the business-facing flag.
- `SysCrmContact.PrimaryCustomerId` is a nullable uniqueness slot.
- When `IsPrimary` is true, the domain service stores the contact's `CustomerId` in `PrimaryCustomerId`.
- When `IsPrimary` is false, the slot is null.
- A tenant-prefixed unique index on `(TenantId, PrimaryCustomerId)` enforces the invariant at the database boundary. Multiple NULL slots are allowed by PostgreSQL and SQLite.

## Migration 5.9.3

- Adds nullable `primary_customer_id` to `crm_contact`.
- Backfills it from `customer_id` for rows with `is_primary = true`; non-primary rows receive NULL.
- Checks for existing duplicate primary contacts within the same tenant/customer before creating the unique index.
- If duplicates exist, raises an explicit migration error. The migration transaction is rolled back and startup stops; operators must resolve duplicate primary rows and retry.
- No contact rows are deleted or automatically demoted during migration.

New databases receive the column and unique index through the entity's SqlSugar CodeFirst mapping. Existing databases receive them through the PostgreSQL migration.

## Runtime behavior and limits

Contact create/update maintains the uniqueness slot. Attempting to mark a second contact primary for the same tenant/customer is rejected by the database unique constraint unless the previous primary is first unset. The current workflow does not silently demote another contact; a dedicated atomic “replace primary contact” command can be added later if the product UX should switch the primary automatically.

## Validation status

- Entity mapping, write-path maintenance and migration regression test source are committed.
- No .NET build/test or PostgreSQL migration execution has been performed yet.
