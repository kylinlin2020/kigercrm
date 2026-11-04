# CRM document-create idempotency: durable request ledger

Status: implementation design; not yet a claim of completed request-level idempotency.

## Current behavior and the gap

Sales orders and shipments currently persist a SHA-256 request fingerprint on the document header. The server-side numbering service gives a stable number for a numbering idempotency key, and a sequential retry can find the document by that number and compare fingerprints.

This protects the sequential retry path only. It does not provide an atomic reservation of a request key before document creation. Two first requests can both observe no document, then race to insert. The unique document number prevents two committed documents with that number, but the losing request is not guaranteed to recover and return the winner. A soft-deleted document is also hidden by the normal repository query filter, even though its permanent number remains unique.

The numbering subsystem already has a durable `Sys_Numbering_Allocation` record with a unique scope over rule, request tenant and idempotency key. It stores the numbering request fingerprint and generated number facts. It is deliberately committed in its own `requiresNew` unit of work before the caller's document transaction. Therefore it is not an atomic CRM-document request ledger and must not be treated as one: its independent transaction is intentional so a number is never reused after a business transaction rolls back.

## Framework transaction behavior verified from repository source

The CRM create entry points are decorated with `[UnitOfWork(true)]` (both direct sales-order create and shipment create). This is the existing aggregate transaction boundary: ledger insertion, document header/line writes and shipment reservation updates must remain inside this same transactional application-service invocation.

The numbering implementation provides an explicit, source-verified contrast. `NumberGenerator.AllocateInTransactionAsync` calls:

```csharp
using var unitOfWork = _unitOfWorkManager.Begin(
    new XiHanUnitOfWorkOptions(isTransactional: true), requiresNew: true);
var result = await AllocateOnceAsync(...);
await unitOfWork.CompleteAsync(cancellationToken);
```

Its XML remarks state that this is deliberately an independent physical transaction when a caller transaction exists, so number allocation commits before the caller's business transaction and numbers are not reclaimed on caller rollback. This confirms that `Sys_Numbering_Allocation` cannot be reused as the CRM request ledger.

The framework UoW implementation itself is not vendored in this repository tree; the application consumes `XiHan.Framework.Uow`. Therefore, before implementing automatic winner recovery, verify the exact deployed package implementation for: (a) whether `Begin(..., requiresNew: true)` always creates a fresh physical transaction in the current tenant database; (b) disposal without `CompleteAsync` rolls back and releases the connection; (c) how PostgreSQL unique violations are surfaced and how the named constraint is identified; and (d) whether a new UoW can safely be opened after the failed UoW is disposed. Do not infer these details solely from the method signatures.

### Repository and schema-discovery constraints verified

- CRM repositories inherit `SaasRepository<TEntity>`, which in turn inherits the framework's `SqlSugarRepositoryBase`. The concrete base implementation is supplied by the external `XiHan.Framework.Data.SqlSugar` package, not vendored here.
- The local `SaasRepository` only adds `AnyIgnoreTenantAsync`, implemented with `CreateNoTenantQueryable()`. That method is explicitly cross-tenant and is not suitable for looking up a request key or document. A new ledger repository must use the normal tenant-filtered query path.
- The repository-local `TenantDatabaseInitializer` delegates tenant database schema creation to the framework `IDbInitializer`, and its documented contract is to build the current entity layout then baseline the upgrade engine. The existing Core CodeFirst tests separately demonstrate that SqlSugar entity attributes determine the actual persisted table/column/index shape.
- CRM's `AddCrmFieldSecurityEntities` is specifically the field-security metadata catalog, not the authoritative database entity registration list. An internal idempotency ledger should not be exposed as a user-editable field-security entity merely to force table creation. Before adding a new entity, verify how the deployed `IDbInitializer` discovers module entities; then add a real CodeFirst shape test and a guarded migration for existing schemas.

## First implementation boundary

The first ledger implementation may safely provide atomic claim + aggregate commit and deterministic conflict on a concurrent duplicate. It must not claim automatic winner replay until a tested outer recovery boundary exists. If the framework cannot expose a reliable rollback-then-new-UoW boundary through the current application-service proxy, return a conflict for the losing concurrent request and let its caller retry; the retry will then find the committed ledger row and replay the document.

This is a deliberate safe intermediate state: it prevents duplicate committed aggregates and preserves request identity, while avoiding a query inside PostgreSQL's aborted transaction. A follow-up can add transparent winner recovery once the framework behavior is proven by PostgreSQL integration tests.

## Decision

Add a CRM-owned request ledger in the same database and transaction as the CRM aggregate. Do not overload `Sys_Numbering_Allocation` and do not attempt a cross-database transaction between the platform numbering database and a tenant's CRM database.

Logical unique key:

- `Tenant_Id`
- `Document_Type` (for example `SALES_ORDER`, `SHIPMENT`)
- `Request_Key` (normalized caller key)

Persist at least:

- request key and document type;
- SHA-256 fingerprint of the normalized business payload;
- document ID and document number;
- creation timestamp / audited metadata.

The unique index must be permanent and must not include soft-delete state. A request key is never released merely because a document is soft-deleted. The ledger should be an immutable mapping after successful completion; a pending reservation must never survive a rolled-back document transaction.

## Required transaction algorithm

1. Normalize and validate the complete business request, calculate the fingerprint, and perform authorization checks.
2. Begin the CRM transactional unit of work.
3. Insert the request-ledger row under the tenant + document-type + request-key unique constraint.
4. Create the document header and lines; for shipments, update order fulfillment reservations in the same transaction.
5. Update the ledger row with the resulting document ID and number.
6. Commit the unit of work. The ledger row and business aggregate become visible together.
7. If the ledger insert loses a unique-key race, allow the failed transaction to roll back completely. Outside that failed unit of work, open a fresh read unit of work and load the committed ledger row:
   - same fingerprint: load and return the existing document (subject to current tenant and data-scope authorization);
   - different fingerprint: reject key reuse;
   - no committed row: rethrow the original conflict because the competing transaction may have rolled back or the database outcome is not yet visible.

Do not catch a unique violation and continue querying in the same PostgreSQL transaction: PostgreSQL marks the transaction failed until rollback. Do not rely on a framework-specific nested UoW behavior until its implementation and transaction ownership have been verified.

## Tenant and storage rules

- The ledger is a tenant-owned CRM entity, not a platform-data-source entity. This keeps it in the same tenant database as the document when tenant database isolation is enabled.
- In shared-database mode, the unique index includes `tenant_id`; all lookups must retain strict tenant filters.
- In tenant-database mode, keep `tenant_id` populated consistently and retain the same unique index for schema parity.
- The unique key must not include `is_deleted`; request keys remain consumed permanently.
- Normal lookup must not expose another tenant's key or document. A replay must re-check the current user's data scope before returning the document.
- A ledger entry with no completed document must never be committed. If the implementation chooses a two-phase reservation instead, it needs leases, expiry, ownership tokens and crash recovery; that is explicitly not the first implementation.

## Migration and CodeFirst

- Add the ledger entity to the CRM module's entity registration so new databases create it through CodeFirst.
- Add a guarded PostgreSQL migration for existing tenant schemas. Follow the repository's lower-case, unquoted identifier convention and guard for the table's presence where platform/tenant schema differences require it.
- The migration must create the permanent unique index and supporting lookup index.
- Update the Windows acceptance checklist for both shared-schema and tenant-isolated deployments.
- Existing document-header fingerprints remain useful for audit/compatibility during rollout, but the ledger becomes the authoritative request-key mapping after migration.

## Required tests before enabling the path

1. Repository-backed persistence and strict tenant isolation for both document types.
2. Same key + same payload sequential replay returns the same document.
3. Same key + different payload is rejected.
4. Two independent PostgreSQL connections submit the same key concurrently: exactly one aggregate and one ledger row commit; the loser rolls back and then returns the winner.
5. Concurrent same key with different payloads: one wins; the other receives a deterministic key-reuse conflict.
6. Inject failure after header insert, after a line insert, and after shipment reservation: no ledger, header, line or reservation partial state remains.
7. Soft-delete the document: the request key remains occupied and replay does not silently create a replacement.
8. Tenant-database and shared-database configurations both pass.
9. Verify actual UoW behavior and PostgreSQL SQLSTATE handling with the repository's real framework package; do not infer behavior from SQLite tests.

## Explicit non-goals

- No distributed transaction with the platform numbering database.
- No guarantee that numbering sequences have no gaps; the numbering subsystem intentionally commits allocations independently.
- No replay for manually numbered creates unless a request key is explicitly supplied and the ledger path is used.
- No automatic retry loop around arbitrary database failures. Only a recognized unique violation on the ledger's exact unique constraint may enter winner recovery.
