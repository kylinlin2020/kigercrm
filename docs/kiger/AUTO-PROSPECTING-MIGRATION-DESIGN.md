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
