# CRM 报价与销售订单模型

## 当前交付范围

本阶段先建立持久化模型，不开放 API 或菜单；前端页面和业务命令在后续提交中按完整契约接入。

| 实体 | 用途 |
| --- | --- |
| `SysCrmQuote` | 报价主记录，按报价编号 + 版本号保存修订历史 |
| `SysCrmQuoteLine` | 报价行项目，保存产品/服务描述和成交条件快照 |
| `SysCrmSalesOrder` | 销售订单主记录，可追溯来源报价 |
| `SysCrmSalesOrderLine` | 订单行项目，从报价行复制商业条款快照 |

四个实体均使用审计实体基类和严格租户隔离，并通过 CRM 的 SaaS 仓储模式访问。它们已加入字段安全实体注册。

## 关键业务约束（后续 Domain/API 必须落实）

1. 报价编号在租户内分配；同一编号允许多个版本，版本号从 1 递增。唯一性范围为租户、报价编号、版本和软删除状态。
2. 已发送或已接受报价不原地改写商业内容；修改应创建新版本并保留上一版本引用。
3. 数量与单价使用四位小数精度；折扣率、税率以百分数表达（0–100）；金额行和汇总使用两位小数。
4. 所有金额由后端根据行项目计算，不接受客户端汇总金额作为可信输入。每行先按货币精度舍入，再汇总；舍入规则固定为 MidpointRounding.AwayFromZero。
5. 报价必须关联当前租户客户；联系人（如有）必须属于同一客户；商机（如有）必须属于同一客户。
6. 报价转订单只能从 Accepted 状态执行。转换必须在一个事务中复制报价头和行项目快照、创建订单并更新报价的 Converted 状态/订单引用。
7. 并发转换必须依赖持久化乐观并发控制或等效数据库约束；不能只依赖进程内锁。失败事务不得留下半张订单或孤儿订单行。
8. 订单保留来源报价、客户、联系人、商机、币种、价格、税率和折扣的快照，后续客户资料或报价变更不得反向改写已创建订单。
9. 所有写操作必须使用 Application Service 的事务边界、权限码、负责人数据范围校验和 CancellationToken；租户过滤在后端仓储层强制执行。

## 状态枚举

- 报价：Draft → Sent → Accepted / Rejected / Expired；Accepted → Converted。
- 订单：Draft → Confirmed → Processing → PartiallyShipped → Completed；Cancelled 为受控终止状态。

实际状态迁移规则由后续 Domain Service 实现，不允许客户端直接任意改写状态字段。

## 数据库初始化

新增实体由当前 BasicApp CodeFirst 初始化流程创建新表；本次没有修改已有表，因此不新增 PostgreSQL 升级脚本。存量数据库的新表创建行为仍需在真实 PostgreSQL 环境验证。

## Quote creation implementation status

The first application command is now wired:

- `ICrmQuoteAppService.CreateAsync` is a transactional application operation with the CRM create permission.
- Both quote header and each quote line pass through field-security create checks.
- Owner assignment is checked against the caller's user data scope.
- Domain validation resolves customer, contact and opportunity through tenant-aware repositories and verifies contact/opportunity ownership against the selected customer.
- A draft quote and all line snapshots are inserted in the same UnitOfWork.
- The server rounds quantity, unit price and rates to persisted precision before computing amounts. Each line subtotal, discount and tax is rounded to two decimals using `MidpointRounding.AwayFromZero`; the header totals are derived from the calculated lines.
- Quote number is supplied by the caller in this initial slice; the tenant-scoped unique index is the final duplicate guard. A dedicated configurable number sequence is a follow-up item.
- A maximum of 500 lines is accepted per quote.

This is source implementation, not a completed runtime verification. Query/list/detail, update-as-new-version, status transitions and quote-to-order conversion remain follow-up work.

## Quote lifecycle implementation status

The quote application service now also exposes an authorized status-change command:

- `Draft → Sent`
- `Sent → Accepted`
- `Sent → Rejected`
- `Sent → Expired`, only after the configured validity date has passed

The status command requires the CRM execute permission, checks record owner data scope and field-level update access, and runs inside a UnitOfWork. Terminal states cannot be changed through this command. Accepted-to-Converted is intentionally reserved for the atomic quote-to-order conversion workflow; it is not exposed as a manual status update.

Source-wiring tests cover these rules, but have not been executed. Concurrent status changes still require a database-backed concurrency strategy before the workflow is considered production-ready.

## Quote list query implementation status

A paged quotation list query is now available through the CRM dynamic API:

- Search by quote number, customer snapshot name or currency.
- Filter by customer, quote status and owner.
- Apply caller owner-data scope and field-level query guards before repository paging.
- Require CRM read permission and default to newest-created first.
- Return quote version, customer snapshot, status, validity, currency and grand total for list views.

Quote detail with line retrieval, version revision, and quote-to-order conversion are not implemented yet. The list query has source-wiring tests only; compilation and database query execution remain unverified.

## Sales order list query implementation status

A paged sales order list dynamic API is now available:

- Search by order number, customer snapshot name or currency.
- Filter by customer, order status and owner.
- Enforce CRM read permission, owner-data scope and field-level query guards before paging.
- Return source quote reference, customer snapshot, order date, currency and grand total.
- Default sorting is newest-created first.

This is a list-query slice only. Sales order creation from accepted quotations, order detail/lines and order lifecycle commands remain pending. Source-wiring tests were added; compilation and database query execution have not been run in this session.

## Sales order lifecycle command implementation status

An authorized sales order status command is now exposed through the CRM dynamic API:

- Draft → Confirmed or Cancelled.
- Confirmed → Processing or Cancelled.
- Processing → PartiallyShipped or Completed.
- PartiallyShipped → Completed.
- Completed and Cancelled are terminal states.

The command requires Execute permission, runs inside a transactional unit of work, validates tenant-scoped order existence through the repository, enforces owner data access and field-level update permission, and rejects all unlisted transitions. Shipment quantities, partial-shipment records, inventory reservation and fulfillment integrations are not implied by status changes; they require dedicated domain records and workflows.

Source-wiring tests were added. Concurrent state-change protection and runtime/database verification remain outstanding.

## Quote and sales order detail query implementation

The quote and sales order query services now expose separate header-detail and paged-line endpoints.

- Header details include commercial totals, customer/contact/opportunity references, validity or order terms, notes, and conversion/source references where applicable.
- Line endpoints require the parent quote/order to exist and be inside the caller's owner-data scope before querying child records.
- Child lines are tenant-scoped through their repositories, filtered by parent ID, field-security guarded, paged, and sorted by line sort order.
- Read permission is required for each detail/line operation.

This avoids loading unbounded line collections into a single detail response. Source-wiring tests cover parent authorization, line scoping and field guards. Version revision, quote-to-order conversion, concurrency control, compilation and database execution remain pending.

## Sales order source-quote uniqueness guard

The sales-order header now has a tenant-scoped unique index over `TenantId + SourceQuoteId + IsDeleted`. Since `SourceQuoteId` is nullable, orders entered independently may leave it null; database engines supported by this project permit multiple null values in a unique index. For a non-null source quote, the index is a final database-level guard against creating more than one order from the same quote, including concurrent requests. The index does not replace application authorization, transaction boundaries, or quote status checks; accepted-quote conversion still needs to create the order and update the quote in one transaction. Existing databases need CodeFirst/index synchronization before this guard is active.

## Shipment fulfillment persistence foundation

The CRM module now includes tenant-scoped shipment headers and shipment-line quantity records.

- Shipment headers reference a sales order and retain a unique tenant shipment number, status, shipment/delivery dates, carrier and tracking number.
- Shipment lines reference sales-order lines and store fulfillment quantity at four decimal places. Sales-order lines maintain reserved and dispatched quantities separately.
- A tenant-scoped unique index prevents duplicate references to the same sales-order line inside one shipment; an additional index supports lookup by order line for future cumulative-shipment validation.
- Both entities are registered with CRM field-security metadata and use the existing SaaS tenant-aware repository base.
- Initial statuses are Draft, Dispatched, Delivered and Cancelled.

Shipment create, cancel, dispatch and delivery commands are implemented with a transactional order-line fulfillment ledger and automatic sales-order fulfillment status roll-up. Warehouse stock deduction and carrier integration remain out of scope. CodeFirst synchronization and runtime database validation remain pending.

## Shipment query endpoints

- Added paged shipment list query with keyword (shipment number, carrier, tracking number), sales-order, status and owner filters.
- Added shipment detail query and paged shipment-line query.
- Read permission, tenant-aware repository filtering, owner data scope and field-security query guards are applied.
- Shipment-line reads first authorize access to the parent shipment, preventing direct line-ID/page access from bypassing parent record scope.
- Shipment list/detail/line read APIs remain tenant-scoped and owner-authorized; write-side fulfillment rules are implemented in the transactional command described below.

## Draft shipment creation command

- Added a transactional Create shipment command with Create permission.
- Validates the source sales order exists in the current tenant, is within the caller's owner data scope, and is Confirmed, Processing or PartiallyShipped.
- Requires 1–500 unique sales-order line references; each line must belong to the selected order and each requested quantity must be positive, use at most four decimal places, and fit within the line's remaining quantity after existing reservations and dispatched quantities.
- In the same UnitOfWork, increments each order line's reserved quantity before inserting the Draft header and shipment lines.
- Each reservation update uses the order-line entity's persistent RowVersion optimistic lock, so concurrent draft creation cannot both reserve the same remaining quantity.
- The shipment owner defaults to the source order owner and is checked against the caller's assignable data scope.

The ledger does not deduct warehouse stock; it tracks customer-order fulfillment only. Dispatch and delivery are separate authorized state transitions.

## Fulfillment state ownership hardening

Manual sales-order status edits cannot set PartiallyShipped or Completed. These states are derived from the shipment dispatch workflow, not client-supplied status changes. Dispatch updates the order to PartiallyShipped or Completed based on all order-line shipped quantities.

## Immutable quote revision command

- Added a Create-authorized, transactional revision API for an existing quote.
- Only Sent, Rejected or Expired source quotes can be revised. Draft, Accepted and Converted quotes are rejected.
- A revision reuses the original quote number and commercial identity (customer, contact, opportunity, currency and owner), increments the version, and records PreviousVersionId.
- The revised terms, dates, notes and full line set are supplied as a new draft; amounts are recalculated by the existing server-side quote creation domain service.
- The tenant-scoped unique index on tenant + quote number + version remains the final duplicate-version guard. Concurrent revision attempts for the same source may race; one will be rejected by the database uniqueness constraint. A dedicated version allocator/locking strategy and runtime database verification remain outstanding.

## Quote revision regression coverage

Expanded source-wiring coverage for revision immutability and authorization: revisions build a new command from the source quote identity, create a new Draft header and newly persisted lines, and do not update the original quote record. The application endpoint checks source record visibility, assignable ownership and field-security creation permissions. Runtime behavior and concurrent same-version attempts still require database-backed tests; the tenant/quote/version unique index remains the final duplicate-version guard.

- Shipment lifecycle regression coverage now verifies Draft cancellation, Dispatched quantity consumption and Delivered timestamp transitions; runtime database-backed concurrency tests remain outstanding.

## Sales-order quote-line uniqueness guard

Added a tenant-scoped unique index over sales order + source quote line + deletion state. Together with the existing unique source-quote index on sales-order headers, this prevents a quote line from being copied twice into the same converted order and prevents a second order from being linked to the same quote. Nullable source quote-line values are intended for manually created order lines; database-specific nullable unique-index behavior must be verified on the deployment database.


## Existing-database index migration

The sales-order source-quote and source-quote-line unique indexes are declared in entity metadata for new databases. Existing databases are not altered by CodeFirst, so PostgreSQL migration `5.9.4/5.9.4.sql` creates both indexes after checking for duplicate active source links. If duplicates exist, startup migration fails with an actionable exception instead of silently installing an incomplete uniqueness guarantee. PostgreSQL is the repository's supported migration dialect; other database engines require equivalent deployment migrations. The indexes allow multiple manually entered lines/orders with NULL source references under PostgreSQL NULL-distinct unique-index semantics.


Migration hardening note: duplicate preflight groups by the complete unique-index key, including `IsDeleted`. This is intentional because the declared indexes include the soft-delete flag; multiple historical deleted rows with the same source reference would also prevent index creation and must be reported before migration.


## Quote version uniqueness migration

The 5.9.4 PostgreSQL upgrade also installs the entity-declared tenant + quote number + version + IsDeleted unique index for existing databases. It checks the complete key (including soft-deleted history) before index creation. This is the database-level final guard against concurrent revision attempts producing the same version. The migration is guarded for tenant databases where the quote table exists; non-PostgreSQL deployments need equivalent DDL.


## Shipment fulfillment command implementation

- Draft creation reserves the requested quantity on each sales-order line and checks the remaining balance: ordered quantity minus already reserved quantity minus dispatched quantity.
- Reservation writes use the existing SqlSugar optimistic RowVersion update path inside the application UnitOfWork. A stale concurrent update raises a concurrency conflict; the transaction rolls back the entire shipment header, lines and earlier line reservations. Each order also increments `FulfillmentRevision` before reservation and on dispatch/lifecycle mutation, serializing competing shipment creation and order cancellation on the same order row.
- Draft cancellation releases its exact reserved quantities in the same transaction. Dispatched shipments cannot be cancelled. Sales orders cannot be cancelled once any quantity is reserved or dispatched; draft shipments must first be cancelled to release reservations.
- Dispatch is allowed only from Draft. It atomically moves each shipment line quantity from reserved to shipped, verifies the source order is still eligible, and updates the sales-order lifecycle to PartiallyShipped or Completed after reading all order lines.
- Delivery is allowed only from Dispatched and records DeliveredDate. Repeated or out-of-order transitions are rejected.
- The 5.9.5 PostgreSQL migration adds the order-level fulfillment revision and non-negative reserved/shipped columns and a database check that their sum never exceeds the ordered quantity. New databases receive the columns from entity metadata.
- Source-wiring regression tests were updated for the ledger, order-level serialization and transitions. Sales-order line query DTOs expose reserved, shipped and remaining quantities. No backend build, automated test run, PostgreSQL migration run, or real-device verification has been observed yet. A concurrent multi-connection integration test remains a release gate.
