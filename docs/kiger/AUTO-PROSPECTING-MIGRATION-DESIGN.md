# 自动获客模块迁移盘点与实施设计

> 状态：源码盘点与目标架构设计；不是已完成的功能迁移。  
> 目标分支：`kiger-dev`。不修改 `main`。PR #1 保持 Draft。

## 1. 迁移目标

将参考项目 `hongshuoguo2-cloud/haituo-crm` 的自动获客能力迁移到 KIGER CRM，复用 KIGER 已有的 Lead、Customer、Contact、权限、租户隔离和跟进流程。

迁移目标不是复制 Node.js/TypeScript 运行时，也不是另建 CRM。应迁移业务能力、领域规则、数据源适配契约和验收标准，并按 KIGER 的 .NET、SqlSugar、PostgreSQL、Dynamic API 与 SaaS tenant 结构重新实现。

## 2. 已确认的源项目能力

从源项目 `agent-skills/prospecting/SKILL.md`、`agent-knowledge/30-prospecting.json`、`backend/src/lead-finder-launch.ts`、`prospect-strategies.ts` 和 `prospect-candidate-pipeline.ts` 确认：

1. **任务启动与编排**：普通搜客 / 深度超级搜索；任务、策略、可选定时计划；启动幂等；返回任务 ID 和分阶段耗时。
2. **搜索策略**：按市场、产品、客户类型、排除域名、来源计划和时间范围组织搜索条件；策略需要审批后激活。
3. **数据源执行**：按已启用 provider plan 执行来源；记录来源级成功、失败和真实错误；不允许无来源计划启动。
4. **原始命中留存**：原始来源记录与 run、team/owner、provider 关联，供后续复核、重放和审计。
5. **候选处理流水线**：规范化、去重、候选合并、处理状态幂等；区分新增、更新、重复抑制和失败。
6. **企业身份与覆盖判断**：官方身份/强标识解析、证据记录、覆盖状态，避免将同一企业重复推入待处理队列。
7. **人工可联系门槛**：只有符合人工可联系条件的候选才允许转为 Lead；转 Customer 必须先有对应 Lead。
8. **长任务完成语义**：任务启动不等于完成；必须持续跟踪至终态，报告原始数量、清洗数量、候选数量和失败来源。

源项目另有 provider credentials、dispatch、queue/worker、run/schedule、campaign、candidate action、contact enrichment 等模块，后续实施前需逐项读取其契约与测试，不能仅凭文件名直接照搬。

## 3. KIGER 目标系统映射

| 源项目能力 | KIGER 目标实现 | 迁移原则 |
|---|---|---|
| Campaign / campaign version | 新增 Prospecting Campaign 聚合 | 租户隔离、版本快照、状态审计 |
| Strategy / provider plan | 新增 Prospecting Strategy | 草稿、审批、激活；不可执行空来源计划 |
| Provider configuration | 新增数据源配置与凭据服务 | 凭据加密；密钥不进入日志/DTO |
| Run / schedule / queue | 新增搜客任务、调度与执行记录 | 可恢复、可取消、可观察；任务幂等 |
| Raw source hit | 新增原始采集记录 | 保留来源、抓取时间、证据与安全边界 |
| Candidate pipeline | 新增候选池与处理状态 | 规范化、去重、合并、失败可重试 |
| Identity / coverage | 新增企业身份证据与覆盖记忆 | 强标识优先；弱匹配不自动合并真实 CRM 客户 |
| Candidate review / conversion | 调用现有 Lead 创建流程 | 人工确认后创建 Lead；不直接绕过 CRM |
| Lead → Customer | 复用现有 Lead 转化工作流 | 不再实现第二套 Customer/Contact |

## 4. 推荐领域边界

建议新建独立模块（暂定 `XiHan.BasicApp.Prospecting`），避免把采集任务、外部凭据和候选流水线塞进 CRM 核心实体。

- **Prospecting**：Campaign、CampaignVersion、Strategy、StrategyApproval、Run、Schedule。
- **Sources**：Provider、ProviderConfiguration、ProviderRequest、ProviderResult、SourceRawRecord。
- **Candidates**：Candidate、CandidateEvidence、CandidateProcessingState、CandidateReview、CandidateConversion。
- **Identity**：OrganizationIdentity、IdentityClaim、IdentityResolution、CoverageMemory。
- **Execution**：任务调度、worker、重试、取消、心跳、限流和成本预算。

上述为建议边界，不代表实体或 API 已创建。最终命名应先核对仓库模块注册、权限种子、数据库连接与后台任务基础设施。

## 5. 安全与数据规则

- 每个 campaign、run、raw record、candidate、identity 和 review 都必须带租户范围；查询必须走严格租户过滤仓储。
- 所有写操作按现有 CRM 权限模型校验；候选查看、审批、转 Lead、配置数据源应使用独立权限码。
- 数据源密钥必须加密存储，读取时按最小权限解密；禁止回传明文凭据或写入日志。
- 外部网页内容视为不可信输入；防 SSRF、内网地址探测、重定向绕过、恶意 HTML 和超大响应。
- 尊重来源网站条款、robots/访问限制、速率限制及适用隐私法规；不绕过登录、付费墙或访问控制。
- 不得伪造公司、邮箱、电话、WhatsApp、地址或来源证据；未知字段保持为空或标记待核验。
- 公司名称相似或仅域名相似不足以自动合并 CRM 客户；强标识冲突必须进入人工复核。
- 只有候选通过人工可联系审核后才允许创建 Lead；Lead 转 Customer 必须走现有 Convert 工作流。
- 启动、来源请求、原始命中处理、候选合并和转 Lead 都需要幂等键及可审计状态。

## 6. 分阶段实施顺序

### Phase A — 基础任务模型
Campaign、版本快照、Strategy、审批、Run、状态查询、取消与幂等启动。先实现可观察的任务生命周期，不先接真实外部来源。

### Phase B — 数据源框架
Provider 契约、配置/凭据安全、来源计划验证、请求记录、限流、错误分类与可替换 fake provider。先以 fake provider 完成端到端测试。

### Phase C — 原始记录与候选流水线
原始记录持久化、规范化、候选池、证据、去重、重复抑制、失败重试和处理状态幂等。明确保留原始证据，不让模型输出覆盖来源事实。

### Phase D — 企业身份与人工复核
强身份标识、身份冲突、覆盖记忆、候选审核队列；弱匹配仅提示，不自动合并 CRM 主数据。

### Phase E — CRM 集成
人工批准后调用现有 Lead 创建服务；复用现有 Lead → Customer/Contact 转化、Owner 数据范围、字段权限和跟进记录能力。

### Phase F — 调度、运行监控与真实来源
完成 worker 恢复、定时执行、成本预算、来源健康度、任务终态报告后，再逐个接入合法且可维护的数据源。

## 7. 必须补齐的测试

- 租户隔离：跨租户不能查看 campaign、run、raw record 或 candidate。
- 权限：无权用户不能查看、审批、配置来源或转化候选。
- 幂等：重复启动、重复 provider 回调、重复处理 raw hit、重复转 Lead 不产生重复记录。
- 恢复：worker 中断后可恢复；单来源失败不阻塞其他来源；取消后不再启动新请求。
- 去重：强标识一致、强标识冲突、仅公司名相似、域名变更等边界。
- 安全：凭据不泄漏、SSRF 拦截、恶意页面内容不执行、响应大小受限。
- CRM：人工审批门槛、Lead Owner 授权、字段权限、Lead 转 Customer 的现有事务约束。
- PostgreSQL：迁移、唯一索引、并发任务领取、事务回滚和多 worker 竞争。
- Windows 实机：拉取 `kiger-dev`、还原依赖、编译、迁移、启动、执行端到端搜客与 CRM 验收。

## 8. 当前状态与不做事项

- 已完成：源项目主要自动获客流程和候选处理入口的第一轮源码盘点；目标 CRM Lead API 已确认可作为转化入口。
- 未完成：模块实体、API、Provider、Worker、UI、迁移脚本和自动获客端到端测试均尚未实现或验证。
- 不迁移源项目的 Node.js/TypeScript 存储实现作为 KIGER 后端运行时。
- 不重复创建 CRM Lead、Customer、Contact，不新增 Go CRM REST Connector。
- 不自动发送开发信或对外联系；未来若增加外联，必须有独立授权和人工审批。
- 不把搜索任务“已启动”报告为“获客完成”。



## 9. Phase A implementation progress (2026-11-04)

The first persistence slice has now been added to `kiger-dev`:

- `SysCrmProspectingCampaign`: tenant-owned campaign objective, product scope, target markets, ideal customer profile, owner and active strategy version.
- `SysCrmProspectingStrategy`: versioned search objective, provider-plan JSON, excluded-domain JSON and approval metadata.
- `SysCrmProspectingRun`: durable execution record with strategy snapshot reference, permanent request key, lifecycle state, timestamps and counters.
- Three typed lifecycle enums define campaign, approval and run states; queued is explicitly distinct from completed.
- Three repository contracts and SqlSugar implementations inherit `SaasRepository`, preserving tenant-scoped access.
- Campaign, Strategy and Run entities are registered with the existing field-security metadata catalog.
- PostgreSQL migration `5.9.11` creates the three tables only in tenant databases that already contain `crm_lead`; platform databases skip the migration. New databases are expected to receive tables through CodeFirst before baseline.
- Campaign and Strategy use full-audit base columns and the required tenant/creation/deletion audit indexes. Run uses the creation-only base and a permanent unique key on (tenant_id, request_key), with no soft-delete column, so deleting an execution cannot release its idempotency key.
- Added source-wiring regression test source for entity bases, tenant scope, repository inheritance, metadata registration, migration/index alignment and permanent run keys.

**Not implemented yet:** campaign/strategy/run application services and API contracts, create/edit/approve/activate commands, permission catalog entries, frontend screens, worker/queue execution, provider adapters and raw candidate storage. The migration and test source have not been executed. The entity and SQL mapping still requires actual CodeFirst comparison and PostgreSQL migration validation before treating the schema as accepted.


## 11. Run lifecycle API slice (2026-11-04)

Implemented on `kiger-dev`:

- Added Run start, detail lookup and cancellation API contracts and application-service operations.
- Run start is accepted only for an Active campaign whose current strategy version is activated, approved, and has a non-empty JSON provider plan.
- The Run snapshots Campaign ID, Strategy ID and Strategy Version. A tenant-scoped permanent RequestKey is used for idempotency.
- Added a SHA-256 `PayloadHash` column (migration 5.9.12). Reusing a RequestKey for the same campaign/strategy snapshot returns the existing Run; reuse after the active strategy snapshot changes is rejected.
- Queued runs can be cancelled immediately; running runs transition to CancelRequested; terminal runs cannot be cancelled.
- API access checks campaign owner data scope and uses existing CRM Execute/Read permission codes.
- Added source-level regression guards for approved/active strategy gating, non-empty provider plan and migration/entity fingerprint mapping.

**Important execution boundary:** this slice persists a `Queued` run only. It does not yet dispatch a worker, perform provider requests, update progress counters, or transition a run to Succeeded/Failed. A queued task is not a completed prospecting job. The 5.9.12 migration, .NET build/tests, PostgreSQL behavior and Windows acceptance have not been executed. Historical Run rows receive an empty fingerprint during migration and are deliberately not considered safe idempotent replays.


## 12. Prospecting history read APIs (2026-11-04)

Added read endpoints for campaign strategy history and execution history:

- `GetStrategiesAsync(campaignId)` returns strategy versions newest-first.
- `GetCampaignRunsAsync(campaignId)` returns runs newest-first.
- Both endpoints verify campaign existence and enforce the existing owner/data-scope access check before reading child rows.
- Both use strict tenant-scoped repositories and the existing CRM Read permission.

These endpoints currently return the complete history for the campaign; paging and server-side filtering should be added before high-volume production use. No worker execution or provider integration is implied by these read APIs.


## 13. Search provider boundary and local smoke adapter (2026-11-04)

Added a provider-neutral contract under `Domain/Prospecting`:

- `ICrmProspectingProvider` identifies an adapter by stable `ProviderCode` and accepts an immutable run/search input snapshot.
- Provider output is represented as raw records (external ID, source, title, company, website, contact, business email, summary), plus failure count and warnings.
- The contract explicitly forbids providers from creating/updating CRM leads. Raw provider output must later pass normalization, deduplication and human review.
- Registered `FakeCrmProspectingProvider` as the initial adapter. It is deterministic, makes no network calls and emits synthetic `.example` records only; it is for local smoke tests, not production lead acquisition.

Important boundary: this commit establishes the adapter contract and a safe synthetic implementation only. It does not yet parse provider plans, invoke providers from a worker, persist raw records, or turn records into CRM leads. No external search provider is configured.


## 14. Worker claim and crash-recovery repository foundation (2026-11-04)

The Run repository now exposes the database primitives required by a durable background consumer:

- `ClaimQueuedAsync` uses a conditional database update for `Queued → Running`; only the worker that wins the status transition receives the claimed row. Duplicate queue delivery and competing workers do not both claim the same run.
- `GetQueuedIdsAsync` lists queued work in creation order within the current strict tenant data scope. A future hosted worker can use it to rebuild queue messages after Redis loss or process restart.
- `RecoverInterruptedAsync` requeues `Running` rows after a process interruption and turns `CancelRequested` rows into `Cancelled`, preserving cancellation intent across a crash.
- All queries and updates go through `SaasRepository` and `CreateQueryable()`; cross-tenant recovery must be orchestrated with the existing `ITenantDataScopeRunner`, not by disabling tenant filters.

This is a repository foundation only. No queue message, hosted worker, provider invocation, raw-result persistence or automatic recovery loop is wired yet. The claim/recovery semantics are source-reviewed but not executed against PostgreSQL; lease expiry, multi-process contention, transaction behavior and Windows runtime validation remain open.


## 15. Queued Run dispatch and fake-provider execution slice (2026-11-04)

The execution path now includes:

- A Redis delayed-queue message containing Run ID and tenant ID; the API enqueues only after the creating unit of work completes.
- A hosted consumer following the existing SaaS export worker pattern: tenant context is restored from the message, the database performs the Queued → Running claim, and duplicate deliveries are ignored.
- Startup recovery iterates the existing tenant data scopes, resets interrupted work and re-enqueues queued Runs. This follows the repository's existing single-active-worker recovery convention; it is not multi-instance-safe.
- Provider plans are interpreted as JSON arrays of provider-code strings (for example, `["fake"]`). Each configured provider is resolved by its stable code and invoked sequentially.
- Provider records and source metadata are persisted to the Run's `Raw_Results_Json`; raw-record count, provider failure count, warnings and terminal status are written together.
- Provider failure is isolated per provider so later configured providers can still run. A Run with records and failures is PartiallySucceeded; no records plus failures is Failed; no failures is Succeeded.
- Candidate_Count intentionally remains zero: normalization, deduplication and human-review candidate creation are not implemented by this worker. Raw provider records are capped at 2,000 per Run and serialized output at 5 MB; excess records cause a partial-success warning rather than unbounded storage growth.

The synthetic `fake` adapter is the only registered provider. This is an end-to-end local execution smoke path, not real-world lead acquisition. External provider configuration, rate limits, credentials, candidate pipeline, human review and Lead conversion remain future phases. No build/test, Redis runtime, PostgreSQL migration or Windows validation has been executed.


## 16. Candidate normalization and existing-Lead duplicate screening (2026-11-04)

- Added a tenant-scoped `Crm_Prospecting_Candidate` table, separate from `Crm_Lead`, with provider identity, source/evidence fields, normalized email/domain keys, review status and optional matched Lead reference.
- The worker normalizes provider records into candidates after provider execution. Company name falls back to provider title; incomplete rows without either are counted as failures and skipped. Scalar fields are bounded before persistence.
- Reuses the existing CRM duplicate normalizers for email and website host, then screens against indexed Lead normalized-email and website-host fields. Matches are marked Duplicate with an explanation; non-matches enter PendingReview. This is a conservative exact-key screen, not fuzzy company-name matching.
- Candidate persistence is replay-aware: existing candidate keys for a Run are skipped after a worker restart, with the unique tenant/run/provider/external-id key as the final database guard.
- Run CandidateCount is populated from persisted candidates, including candidates already saved by an interrupted attempt. Candidate rows remain separate and do not create CRM Leads.
- Migration 5.9.14 adds the candidate table only when the CRM Lead and prospecting Run tables exist. Candidate list/detail API, manual approve/reject and approved conversion to Lead remain future work.
- No .NET build/test, PostgreSQL migration execution, duplicate-screening integration test or Windows validation has been performed.


## 17. Human-review and controlled Lead conversion API (2026-11-04)

- Added a tenant/data-scope protected candidate list per Run, optionally filtered by status and capped to the newest 500 rows.
- Added explicit review decisions: PendingReview → Approved or Rejected. Duplicate candidates cannot be approved unless the caller sets the explicit duplicate-override flag.
- Added a separate approved-candidate-to-Lead command. It creates the Lead through the existing `ICrmLeadDomainService`, applies Lead field-creation security, assigns the campaign owner, and updates candidate status/Lead ID in the same UnitOfWork.
- Repeated conversion is rejected by candidate state; transaction rollback should prevent a Lead without a Converted candidate marker, but this still needs a PostgreSQL integration test.
- The workflow remains human-gated. Candidate discovery never writes directly to `Crm_Lead`.
- API wiring is source-reviewed only; compilation, authorization integration, transaction rollback, PostgreSQL and Windows tests remain outstanding.


## 18. Candidate deduplication and cancellation boundary hardening (2026-11-04)

- Candidate screening now also detects repeated normalized email or website host within the same Run, in addition to exact matches against existing tenant Leads.
- Duplicate candidates retain an internal matched Lead reference for audit, but candidate DTOs do not expose that Lead ID across owner data scopes.
- Before candidate normalization, the worker re-reads Run state and skips candidate creation if cancellation was requested. During normalization it checks cancellation between records; if cancellation arrives mid-batch, already persisted candidate rows remain in the database but the Run is finalized Cancelled. This partial-write edge remains a candidate for transactional batching.
- Raw JSON size is validated before candidate persistence, avoiding candidate rows from an oversized provider response that cannot be stored as the Run snapshot.
- Source-level tests now cover the candidate table/repository registration and human-review boundary. No tests were executed.


## 19. Cancellation result accounting (2026-11-04)

When cancellation wins the race against Run completion, the repository now persists the raw provider snapshot and actual raw/candidate/failure counters while setting the terminal Cancelled state. This keeps partial work auditable and the Run counters consistent when cancellation arrives during candidate normalization. The worker still checks cancellation between provider calls and candidate records; it does not forcibly interrupt a provider that ignores cancellation or expose partial candidates as approved.


## 20. Prospecting frontend management surface (2026-11-04)

- Registered the Prospecting page in the single CRM PageRegistry at `/crm/prospecting`, with resource-scoped Create and Execute button permissions and a backend page/view wiring test.
- Added typed frontend API contracts and route tests for campaigns, strategy versions, run history, candidate review and approved-candidate conversion.
- Added a responsive Vue management view for creating campaigns, creating/submitting/approving/rejecting/activating strategy versions, starting/cancelling runs, selecting run history, reviewing/approving/rejecting candidates, explicit duplicate override confirmation and converting approved candidates into existing CRM Leads.
- Added prospecting navigation and interface strings in all seven CRM locales.
- Campaign creation now defaults the owner to the current user when no owner is supplied; campaign listing applies the existing accessible-user data-scope filter and is capped at 500 records.
- UI explicitly warns that only the synthetic `fake` provider is currently configured; no live internet search is performed.
- This is source implementation only. Frontend type-check/build/tests, backend compilation/tests, menu seeding in a real tenant, PostgreSQL/Redis execution and Windows acceptance have not been run.


## 21. Immutable execution input snapshot (2026-11-04)

A queued Run now stores the exact Campaign and Strategy inputs accepted at creation time: search objective, product scope, target markets, ideal-customer profile, provider plan and excluded domains. The request fingerprint is calculated from this serialized snapshot rather than only Campaign/Strategy IDs. The worker reads only the Run snapshot, so editing a Campaign or creating/activating a later Strategy cannot silently change the meaning of an already queued task.

Migration 5.9.15 adds the snapshot columns for existing CRM tenant databases. Historical Runs are not backfilled from mutable Campaign rows because that would falsely claim to reproduce their original inputs; the migration defaults provider plan to an empty array and search objective to an empty string. Existing queued/interrupted Runs created before this migration may therefore fail safely at execution and should be completed before upgrade or re-created after operator review. No PostgreSQL migration execution or runtime test has been performed.


## 22. Transactional candidate-to-Lead claim (2026-11-04)

Candidate conversion now transitions Approved → Converting inside the same transactional UnitOfWork before invoking the existing CRM Lead domain service, then transitions Converting → Converted with the created Lead ID. A failed Lead creation rolls the claim back with the transaction; the entity optimistic-lock update is the concurrency guard against two requests converting the same candidate. The Converting state is an internal in-flight state and must not be treated as a terminal review decision. Database-backed two-request race and rollback tests are still required before production acceptance.


## 23. Candidate status enum data migration (2026-11-04)

The existing persisted enum had Converted = 4. The new in-flight Converting state occupies 4 and Converted is now 5. Migration 5.9.16 updates existing candidate rows with status 4 to status 5, guarded by candidate-table existence. Deploy this migration with the application stopped or under a coordinated deployment; do not run old and new enum versions concurrently. Source-level migration assertions were added; PostgreSQL execution is not yet verified.


## 24. Atomic candidate claim repository test source (2026-11-04)

Added a SQLite integration-test source that initializes the real candidate entity/repository and verifies: an Approved candidate can be claimed once, a repeated claim returns false after the status becomes Converting, and the conditional claim respects strict tenant filtering. This is stronger than a source-text assertion because it exercises the actual SqlSugar repository against SQLite when the test is run. It is still sequential rather than a simultaneous two-connection race, and SQLite does not validate PostgreSQL locking, transaction rollback, or migration behavior. The test has been committed but not executed.


## 25. Prospecting Run repository SQLite integration test source (2026-11-04)

Added repository-level SQLite integration test source covering four state/tenant boundaries: a Queued Run can be claimed once (duplicate claim returns null); a persisted CancelRequested state wins over a later CompleteAsync success attempt and becomes Cancelled; interrupted recovery returns Running to Queued while finalizing CancelRequested as Cancelled; and strict tenant filtering prevents another tenant from reading or claiming the Run. These tests exercise the actual SqlSugar repository and CodeFirst entity mapping when executed. They have not been run. They do not prove simultaneous PostgreSQL claims, PostgreSQL transaction rollback/isolation, or safe multi-instance startup recovery. The worker must remain single-instance until durable lease/heartbeat/fencing is implemented and tested.


## 26. Recovery result contract correction (2026-11-04)

Code review found that RecoverInterruptedAsync returned an int computed from two boolean UpdateAsync results. That value represented the number of transition categories that changed (0–2), not the number of Runs recovered, despite the int return type being easy to misread as a row count. The repository contract now returns bool: true means at least one recovery transition occurred, false means neither transition changed any row. SQLite repository test source now asserts true on the first recovery pass and false on the immediate second pass. This does not change the single-active-worker restriction and has not been compiled or executed.


## 27. Prospecting history response bounds and source-guard correction (2026-11-04)

The strategy-history and campaign-run-history application endpoints now cap returned items at the newest 500 entries, matching the existing 500-item candidate and campaign response convention. This limits response size but the current GetListAsync calls still materialize the matching rows before ordering/taking; database-side pagination remains follow-up work for large tenants.

During the same review, the source-wiring test still asserted an obsolete conversion comment ("Claim the candidate inside this transactional UoW") that had been replaced by the explicit conditional database-update concurrency-boundary comment. The assertion now checks the current implementation wording. This source-level correction has not been compiled or executed.


## 28. Database-side bounds for strategy and Run history (2026-11-04)

Strategy history and campaign Run history now use dedicated repository queries. Each query applies tenant-scoped CreateQueryable filtering, descending order, and Take(limit) before ToListAsync, so the 500-row response cap is applied by the database rather than after materializing all matching rows. Repository methods clamp requested limits to 1–500. AppService maps only the bounded result set. Existing API response shape remains IReadOnlyList and no total count/cursor is introduced in this pass.


## 29. Database-side bounds for candidate review list (2026-11-04)

Candidate listing now uses ICrmProspectingCandidateRepository.GetLatestByRunAsync(runId, status, limit). The repository applies the strict tenant-scoped query, optional enum status filter, descending CreatedTime order, and Take(limit) before ToListAsync. The requested limit is clamped to 1–500. The AppService retains the existing optional integer status API and validates the enum value before converting it to the domain enum. This avoids loading every candidate for a Run merely to return the newest 500.


## 30. Database-side bound for Campaign list (2026-11-04)

Campaign listing now calls ICrmProspectingCampaignRepository.GetLatestAccessibleAsync with the resolved data-scope result. The repository returns the newest 500 rows, applying the CreatedTime descending order and Take(limit) in the tenant-scoped database query. For restricted scopes, the accessible owner IDs are de-duplicated and applied as an OwnerUserId IN predicate; an empty accessible-user set returns an empty list without querying. Unrestricted scope omits the owner predicate. This preserves the existing data-scope behavior while avoiding materializing every visible Campaign. Limit is clamped to 1–500.

## 31. Worker deployment safety gate and candidate write characteristics (2026-11-04)

The current hosted worker is safe only under a single-active-process deployment constraint. `UpdateMaxConcurrentTasks(1)` limits concurrency inside one hosted-service instance; it is not a distributed singleton guarantee. The conditional database `Queued → Running` claim prevents two consumers from successfully claiming the same queued Run, but it does not make startup recovery multi-instance-safe: `RecoverInterruptedAsync` changes every tenant-visible `Running` row back to `Queued`. If another live worker is executing a Run, a second instance starting up can therefore reset that live Run and enqueue it again. The original worker may continue processing while a second worker later claims the reset row.

**Deployment gate:** do not run more than one application/worker instance with prospecting enabled. This is an operational restriction, not an enforced distributed lock. Do not treat per-process max concurrency or the conditional claim as a substitute for a cross-instance lease.

**Required multi-instance design before horizontal scaling:** add a durable worker lease with owner identity, expiry/renewal heartbeat and a monotonically increasing fencing token. Recovery must reclaim only expired leases; every state/result write from a worker must be conditional on its current fencing token. Queue messages remain hints and the database lease is authoritative. Add PostgreSQL multi-connection tests for simultaneous claim, lease expiry, stale-worker fencing and cancellation/recovery races before enabling multiple instances.

Candidate normalization still persists each candidate with an individual repository `AddAsync` call, but the N+1 existing-Lead duplicate lookup has been removed. The worker now normalizes the raw batch's distinct email/domain keys up front and calls `ICrmLeadRepository.GetDuplicateMatchesAsync`. That repository queries the tenant-scoped indexed normalized-email and website-host columns in chunks of 300 keys, unions matches by Lead ID, and the worker selects only matches relevant to each candidate. This reduces duplicate-screening queries from up to one per raw record to a bounded number of chunk queries (at most 14 for 2,000 distinct email and 2,000 distinct website keys), while preserving tenant filters and the existing email-first match preference.

Candidate writes remain row-by-row. The next persistence optimization requires verifying the inherited repository/SqlSugar batch and transaction APIs and adding retry/unique-key tests proving partial batches are safe. Do not introduce a guessed bulk API. No lease or PostgreSQL concurrency test is implemented; the single-worker restriction remains in force. A SQLite integration-test source now exercises the actual Lead repository batch lookup for email/domain matches, unrelated-record exclusion and strict tenant isolation; this test source has not been executed. Build, tests and runtime validation have not been executed.


## 32. Batch duplicate lookup boundary tests (2026-11-04)

The SQLite Lead repository integration test now additionally covers a lookup containing 301 distinct normalized email keys, with the only matching Lead in the second 300-key chunk. This protects the repository's chunk boundary behavior rather than testing only a single small IN predicate. A pre-cancelled token test also verifies cancellation is observed before the first query. These are test-source additions only: they have not been compiled or executed, and SQLite coverage does not replace PostgreSQL provider validation. The existing test also exercises email/domain union, unrelated-record exclusion and strict tenant isolation. Candidate persistence itself remains row-by-row; no bulk-write behavior is claimed.


## 33. Worker cancellation polling and persistence follow-up (2026-11-04)

The candidate-normalization loop previously reloaded the Run row for every raw record. It now polls persisted cancellation every 25 records (including immediately before processing the first record), reducing the worst-case normalization polling from 2,000 reads to 80 for a 2,000-record Run. The host CancellationToken is still checked on every iteration. The final CompleteAsync transition remains conditional on Running and explicitly lets CancelRequested win, protecting cancellation that arrives after the last poll. A source-wiring regression assertion guards the interval and loop structure. Runtime cancellation tests are still required for cancellation before the first record, between polling boundaries, and immediately before completion; verify that already persisted candidates remain counted and the Run ends Cancelled.

Candidate writes remain one AddAsync call per row. The visible SaaS repository base delegates to the framework SqlSugarRepositoryBase, while the project-local repository interfaces do not expose a batch insert contract. Do not guess an inherited bulk API. Before implementing batching, inspect the exact framework package/source version and verify transaction participation, generated IDs, tenant/audit filling, soft-delete conventions and unique-key conflict behavior. Add retry tests proving previously inserted candidates are skipped and no duplicate candidate rows are created.

No cancellation-polling implementation, bulk insert, build, test execution, PostgreSQL validation or Windows runtime validation is claimed by this review note. The single-active-worker deployment restriction remains until durable lease/heartbeat/fencing is implemented and tested.


## 34. Multi-instance worker lease and fencing design (2026-11-04)

The current startup recovery is intentionally single-worker only: RecoverInterruptedAsync changes every tenant-visible Running row back to Queued. A second application instance starting while the first is legitimately processing a Run can therefore steal active work. The conditional Queued -> Running claim prevents two consumers from claiming the same queued row at the same instant, but it does not make the current startup recovery safe across replicas.

The existing SaaS ExportTaskHostedService has a similar startup reset pattern (Processing -> Pending). It is a legacy operational pattern, not evidence that the prospecting worker is safe for multi-instance deployment. Do not copy it as the lease design.

Required database-authoritative lease model:

- Add nullable Lease_Owner_Id (bounded process-instance identifier), Lease_Expires_At (UTC timestamp), and a non-null monotonically increasing Fencing_Token (bigint, default 0) to each Run. A worker process creates one random owner ID at startup; it must not reuse a deployment name or hostname as the unique owner identity.
- Claim is one atomic conditional database operation: a Queued Run may be claimed, or a Running Run may be reclaimed only when its lease is expired. The operation sets Running, owner, expiry and increments Fencing_Token in the database (not read/increment/write in application memory). The returned claim tuple is RunId + owner ID + fencing token.
- Renew periodically (for example, every lease-duration / 3) using a conditional update matching RunId + Running + owner ID + fencing token. If renewal affects no row, the worker has lost ownership and must stop processing. Choose lease duration based on provider timeout and shutdown budget; do not hold a lease indefinitely during an unbounded provider call.
- Startup recovery must never reset all Running rows. It may only enqueue Queued rows and Running rows whose lease has expired. Redis queue messages remain delivery hints; the database claim is authoritative.
- Every Run mutation (heartbeat, completion, failure, cancellation finalization and raw-result persistence) must include the current owner ID and fencing token in its predicate. A stale worker with token N must not overwrite state after another worker has acquired token N+1.
- Candidate writes must also be fenced. A pre-insert lease check alone is insufficient because the lease can be stolen between the check and insert. Use a database transaction that locks/verifies the Run row and its current owner/token before writing the candidate batch; the competing claim must serialize on that same row. Verify the exact SqlSugar/PostgreSQL transaction and row-lock APIs before coding. Candidate uniqueness remains a second idempotency guard, not a substitute for fencing.
- After provider SearchAsync returns, revalidate/renew ownership before processing its untrusted result. Provider calls are read-only; if future providers introduce external side effects, they need their own idempotency key and must not rely on the Run lease alone.
- On host shutdown, stop renewing, attempt a short bounded graceful cancellation, and leave the lease to expire if ownership cannot be cleanly released. Never clear another worker's lease.

Required tests against PostgreSQL using independent connections/process-like owners: simultaneous first claim yields one winner; active lease blocks a second claim; expired lease can be reclaimed with a strictly larger token; old owner cannot renew/complete/fail/write candidates after reclaim; renewal-vs-expiry race has one serialized outcome; cancellation-vs-reclaim race is deterministic; tenant isolation holds for all lease operations; process death recovers only after expiry. SQLite source tests are useful for query shape but do not prove PostgreSQL locking or concurrency semantics.

Implementation is deliberately deferred until the framework's SqlSugar package version, conditional update expression support, transaction scope and PostgreSQL row-lock syntax are verified. Do not claim multi-instance safety until the database-backed race tests pass. Until then, keep prospecting execution to one active application worker and retain the current single-worker recovery behavior.


## 35. Persisted cancellation after provider execution (2026-11-04)

The worker already checked persisted CancelRequested before starting each provider. It now also reads the Run state immediately after each provider SearchAsync returns and stops the provider-plan loop when cancellation was requested. This prevents launching subsequent providers and reduces unnecessary normalization work after a long-running provider returns. The provider call itself is not forcibly interrupted by a persisted user cancellation: only the host CancellationToken is passed to SearchAsync. Providers with long network calls must implement bounded request timeouts and cooperative host-token cancellation; a future provider-cancellation contract may be needed if user cancellation must abort an in-flight request. After the provider loop, the existing pre-normalization state check finalizes a cancelled Run without starting candidate normalization.

A source-wiring assertion guards the post-provider state check. Runtime tests still need to prove cancellation during provider execution, before the next provider, and before candidate normalization. No provider timeout or runtime cancellation behavior is claimed as tested.

## 36. SqlSugar batch-persistence API verification (2026-11-04)

A repository-level API audit was performed before attempting to replace candidate row-by-row inserts:

- `CrmProspectingCandidateRepository` inherits the project-local `SaasRepository<TEntity>`.
- The local `SaasRepository<TEntity>` is only a thin subclass of the framework `SqlSugarRepositoryBase<TEntity, long>`; it does not define or expose a project-local batch-insert method.
- `ISaasRepository<TEntity>` inherits the framework `IRepositoryBase<TEntity, long>`. The candidate repository interface likewise has no batch-write contract.
- The CRM module references the existing SaaS module, and the normal standalone solution selects the XiHan framework NuGet packages rather than framework source. The framework web project references XiHan Framework 4.8.0; the exact SqlSugar repository implementation and its package dependency graph are not vendored in this repository.
- The project-local code proves that `AddAsync(entity, cancellationToken)` is available, but it does not prove the existence or semantics of `AddRangeAsync`, SqlSugar bulk copy, transaction participation, audit/tenant field filling, generated IDs, or soft-delete handling for a bulk path.

**Decision:** no speculative bulk API was added. Candidate persistence remains one `AddAsync` per candidate. Before batching, resolve the exact 4.8.0 framework package/source and inspect the actual `SqlSugarRepositoryBase` and `IRepositoryBase` implementations, then verify the chosen API against PostgreSQL with (1) a mixed tenant/audit-field batch, (2) a duplicate unique-key inside and across batches, (3) a failure midway through a batch with transaction rollback, and (4) retry after a partially completed worker attempt. The existing per-Run unique key and replay skip must remain the final idempotency guards.

This audit confirms the reason batching is deferred; it does not establish that the framework lacks a batch API. No source-code behavior changed in this audit. No build, tests, PostgreSQL integration or Windows execution was performed.

## 37. Provider exception boundary and persisted-state failure handling (2026-11-04)

A worker control-flow review found that the post-provider Run-state read was inside the same broad `catch (Exception)` used to isolate provider failures. If that database read failed, the worker could misclassify the state-store failure as a provider outage, record a warning and continue launching later providers. This weakened the cancellation and execution-state boundary.

The worker now catches exceptions only around `provider.SearchAsync`. A provider request failure is counted and isolated, then the loop continues to the next configured provider. Result accounting, raw-record retention and the post-provider Run-state read are outside that provider catch. A failure reading persisted Run state therefore reaches the outer Run failure handler instead of being silently treated as a provider failure. The post-provider cancellation check still stops subsequent providers when CancelRequested is observed.

Added a source-wiring guard that verifies the provider call/catch/continue sequence precedes the post-provider state read. This is a structural regression guard only; it does not simulate database faults or prove runtime exception behavior. No build or test execution was performed.

## 38. Bound provider-supplied warning aggregation (2026-11-04)

Provider result warnings are untrusted input. Previously the worker appended every non-empty warning to an in-memory list, joined the entire list, and only then truncated the final persisted summary to 2,000 characters. A provider could therefore return a very large warning collection or oversized strings and cause avoidable memory allocation even though almost all text would later be discarded.

The worker now accepts at most 100 provider warnings in the aggregate list and truncates each accepted warning to 500 characters before storing it in memory. The existing final Run failure-summary limit remains 2,000 characters. A source-wiring assertion guards these bounds.

This is a bounded diagnostic-output safeguard, not a full provider-response memory limit: the provider still constructs its result before returning, and the 2,000-record cap / 5 MB serialized raw-result cap remain separate controls. No build or runtime test was performed.


## 39. Bound every worker-generated warning (2026-11-04)

The previous warning cap covered only the provider-supplied warning collection. Other warning paths still appended interpolated provider exception messages, missing-provider diagnostics, raw-result-cap messages and invalid-record diagnostics directly. Exception messages are also untrusted text and may be unexpectedly large. Repeated direct appends could bypass the intended aggregate count/length bound.

All worker warning paths now use one bounded append helper. The shared limits are 100 non-empty warnings and 500 characters per warning, applied before the warning is retained in the aggregate list. Provider-supplied warning lists use the same helper, so the limit is global across warning sources rather than a separate allowance for each source. The final persisted summary remains capped at 2,000 characters. A source-wiring guard asserts the configured limits and that the helper is the only direct writer to the warning list.

This limits the worker's retained diagnostic strings; it does not cap the provider's own allocations before SearchAsync returns, the raw provider records, or exception object internals. It is not a substitute for provider request timeouts and response-size limits. The guard is source-level only. No build, unit test, runtime, PostgreSQL or Windows test was executed.


## 40. Reconfirm startup recovery is unsafe against a live peer (2026-11-04)

A fresh source-level trace of the current implementation confirms the multi-instance hazard and narrows its impact:

- CrmProspectingRunHostedService.ExecuteAsync invokes RecoverPendingAsync before entering the background polling loop.
- Recovery iterates tenant data scopes, calls RecoverInterruptedAsync, then republishes queued IDs to Redis.
- In each current tenant scope, CrmProspectingRunRepository.RecoverInterruptedAsync conditionally updates every Running row to Queued (run.Status == Running); there is no owner, heartbeat, age threshold, lease expiry or process identity predicate.
- The same method converts CancelRequested to Cancelled, preserving cancellation intent.
- ClaimQueuedAsync conditionally transitions Queued to Running. This prevents two claimers from winning the same queued state transition, but it does not prevent a second process's startup recovery from changing a live worker's Running row back to Queued.
- The worker writes candidate rows with candidates.AddAsync(...) during normalization. Those inserts do not carry a lease/fencing token or atomically validate the Run's ownership. A reclaimed Run can therefore have two workers reach candidate persistence; final CompleteAsync state predicates do not fence earlier candidate writes.

**Operational boundary remains unchanged:** run only one active application instance that hosts prospecting execution. Do not rely on Redis queue delivery or the conditional Queued-to-Running claim as a multi-instance lock.

**Implementation decision:** do not add a timestamp-only stale threshold or a process-local mutex; neither is a correctness boundary across hosts. Before enabling multiple workers, implement database-authoritative lease ownership with expiry/heartbeat and a monotonically increasing fencing token, and ensure every candidate/result/status write validates the current token in the same database transaction as the write. Verify claim/reclaim/renew races and a stale worker attempting candidate insertion against PostgreSQL using separate connections/processes. Framework SqlSugar transaction and row-lock semantics still require source/package verification before implementation.

This section is a source review, not a runtime proof. No build, automated test, PostgreSQL test or Windows test was executed.


## 41. Bound persisted Run failure summaries (2026-11-04)

A worker failure-path review found that the outer exception handler passed the full exception type and message to FailAsync, while Run.FailureSummary is a 2,000-character database column. An oversized provider/persistence/framework exception message could make the attempt to persist the Failed state fail as well, leaving the Run non-terminal.

The worker now truncates its formatted failure summary to 2,000 characters before calling FailAsync. The repository independently enforces the same bound for both CompleteAsync and FailAsync, so callers outside this worker cannot accidentally send an overlength summary to the mapped column. The source-wiring test checks both guards.

This bounds the value sent to persistence; it does not limit the original exception object's message allocation or logging payload. The regression guard has not been compiled or executed. No .NET build, unit test, PostgreSQL integration or Windows real-machine test was performed.


## 42. Transaction and concurrency API evidence gate (2026-11-04)

A follow-up repository inspection confirmed the CRM repositories inherit `SaasRepository<TEntity>`, which in turn inherits the external `XiHan.Framework.Data.SqlSugar.Repository.SqlSugarRepositoryBase<TEntity, long>`. The framework implementation is not vendored in this repository; the CRM module project file also has no direct SqlSugar package reference. The public repository contract exposes `IRepositoryBase<TEntity, long>`, but the checked-in source does not establish the exact transaction callback, connection-sharing, row-lock, conditional update affected-row, or batch insert semantics of the packaged framework version.

The checked-in worker resolves scoped repositories directly and does not establish an explicit Unit of Work around candidate normalization and persistence. The app service uses `[UnitOfWork(true)]` and `IUnitOfWorkManager` for request-side operations, but this is not proof that a background worker can safely compose multiple repository calls in one transaction. The existing workflow runtime helper reads a row's `RowVersion` before calling `UpdateAsync`; this is an optimistic update convention, not evidence of a database lease, heartbeat, fencing token, or atomic candidate-write protocol.

Decision: do not implement lease/fencing or claim transactional candidate writes using guessed framework methods. Before implementation, obtain the exact XiHan Framework package source/XML/API for the version resolved by restore, verify whether all repositories in a unit of work share the same connection/transaction, and prove conditional update affected-row semantics. Then implement a repository-level operation that atomically validates Run status + lease owner + fencing token and writes each candidate in that same transaction. A prior lease read followed by `AddAsync` is explicitly insufficient.

Required verification remains PostgreSQL-backed with independent connections/processes: concurrent claim single winner; live lease exclusion; expiry takeover with strictly increasing fencing token; stale owner cannot renew/complete/fail/write candidates; cancellation/reclaim races; tenant isolation; and rollback of candidate writes when ownership validation fails. No build or runtime tests were performed during this source inspection.


## 43. Enforce the raw-result payload cap in UTF-8 bytes (2026-11-04)

The worker serializes provider records to JSON and enforces a 5,000,000-unit storage guard before persisting the payload. A source review found that the guard compared .NET string Length, which counts UTF-16 code units rather than the UTF-8 byte size of the JSON payload. For predominantly non-ASCII provider data, the previous check could accept a payload whose UTF-8 representation exceeded the stated 5 MB limit.

The guard now uses Encoding.UTF8.GetByteCount(serialized), so the measured value matches the encoded payload byte size. The source-wiring regression test asserts that the worker uses this byte-count check. This is a pre-persistence size guard; it does not bound allocations made while a provider constructs its result or while JSON serialization is occurring. No build or test execution was performed, and the test assertion remains source-level until the .NET suite is run.


## 44. Keep the synthetic provider disabled unless explicitly opted in (2026-11-04)

A production-safety review found that `FakeCrmProspectingProvider` returned synthetic `.example` companies and addresses but was registered unconditionally by the CRM module. Its source comment said not to use it in production, but the registration did not enforce that boundary. A campaign whose provider plan named `fake` could therefore persist invented contacts in a production tenant and expose them in the human-review queue as if they were acquired prospects.

The CRM registration now adds the fake provider only when the explicit configuration key `XiHan:CRM:Prospecting:EnableFakeProvider` parses as `true`. Missing, malformed, or false values leave the provider unregistered. The CRM module passes its application configuration into the registration extension, and a source-wiring regression assertion guards the opt-in key and conditional registration.

This is a fail-closed registration default, not a real acquisition integration. With the setting absent, a Run configured with provider code `fake` will report that the provider is not registered; no synthetic prospects are generated. Enable the setting only in isolated local/test environments. Production still has no real external provider registered. No build, test, or runtime validation was performed for this change.

## 45. Candidate review must be a conditional database transition

A review endpoint previously loaded a PendingReview or Duplicate candidate, checked the state in application memory, then called an unconditional entity UpdateAsync. Two reviewers could both read the same eligible state and both write a decision; the later write could silently replace the earlier decision. The explicit duplicate-approval override was also only checked against the stale in-memory state.

The repository now exposes TryReviewAsync. It performs one conditional update keyed by candidate ID and eligible persisted status: PendingReview is reviewable; Duplicate is reviewable for rejection without override, but approval from Duplicate requires overrideDuplicate=true. The update writes only Approved or Rejected. A false result means the row no longer satisfies the transition predicate, and the application returns a refresh/retry conflict instead of overwriting the first decision. The application retains its preflight validation and field-security check, but those checks are not the concurrency boundary; the conditional database update is.

This is source-level hardening only. The framework UpdateAsync affected-row/boolean semantics must be verified against the resolved framework package, and PostgreSQL tests with independent connections must prove exactly one winner for concurrent review, duplicate approval denied without override, duplicate rejection allowed, and no state regression after a completed review. No build or runtime test is claimed here.
