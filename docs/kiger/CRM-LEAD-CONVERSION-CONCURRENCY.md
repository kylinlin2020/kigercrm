# Lead conversion concurrency audit

The conversion flow has an existing platform-level optimistic concurrency mechanism; it does not need an invented in-process lock.

Verified source path:

1. `SysCrmLead` inherits `BasicAppFullAuditedEntity`.
2. `BasicAppFullAuditedEntity` inherits `SugarMultiTenantFullAuditedEntity<long>`.
3. The framework's `SugarFullAuditedEntity<TKey>` maps `RowVersion` to `Row_Version` and marks it with `IsEnableUpdateVersionValidation = true`.
4. The framework's `SqlSugarRepositoryBase.UpdateAsync(entity)` reloads the row and calls `ExecuteCommandWithOptLockAsync(true)`; a stale version is translated to `ConcurrencyConflictException`.
5. Lead conversion runs inside the application service's `[UnitOfWork(true)]` boundary. Customer/contact inserts and the final lead update therefore share the unit of work.

Expected concurrent behavior, subject to integration verification:
- Two requests may both initially read the same lead and create their own customer/contact rows inside separate transactions.
- Only the request whose lead row-version update wins should commit.
- The losing request should receive an optimistic concurrency conflict; its transaction must roll back its newly inserted customer/contact rows.

This is materially stronger than a plain check-then-insert and avoids provider-specific raw lock SQL. It is not yet marked production-verified: an integration test must run two concurrent conversions against each supported database provider and assert exactly one converted lead plus exactly one committed customer/contact set, with no orphan rows from the losing transaction.
