# KIGER CRM 全仓库结构与重构审计 — 2026-11-08

> 审计分支：`kiger-dev`（基准提交 `2647b5349015100a42dda3c53eefcf7773f6bc3c`）
> 本文用途：建立结构、职责、复杂度和重构顺序的单一审计基线。
> 方法：递归扫描仓库树、统计模块/项目/文件规模；重点阅读 CRM 自动获客 Worker、仓储、模块注册、CSV 导入、Chat/WhatsApp、测试及迁移文档；对四条开放 PR 检查差异和分支基线。
> 限制：这是全仓库结构扫描 + 核心路径深度静态审查，不等于对全部 2,299 个 C# 文件逐行人工审查；当前环境没有 .NET SDK、pnpm，也未连接 PostgreSQL，因此不宣称构建、测试或数据库验收通过。

## 1. 总体结论

项目的主架构是合理的：Saas 提供平台底座，CRM 是独立业务模块，Chat/AI/Workflow 等是独立模块，前端按 API、views、module、shared packages 分层。没有发现应该再造一套 Customer/Lead/Contact CRM 的理由；已有 CRM 应继续作为唯一业务主数据系统。

目前最明显的问题不是缺少更多模块，而是**少数核心文件承担过多职责、注册入口过于集中、审计文档和源代码测试重复增长、部分 PR 以大量小提交和静态文本断言堆叠进度**。下一阶段应先收敛职责和测试证据，再继续扩展功能。

## 2. 仓库结构基线

### 后端

- `backend/src/framework/XiHan.BasicApp.Core`：BasicApp 公共实体/DTO/应用基础能力。
- `backend/src/framework/XiHan.BasicApp.Web.Core`：Web 公共接线。
- `backend/src/modules/XiHan.BasicApp.Saas`：租户、身份、组织、权限、配置、日志、文件、导入历史和通用导出等平台底座。
- `backend/src/modules/XiHan.BasicApp.Crm`：客户、联系人、线索、商机、活动、任务、报价、订单、出货、自动获客。
- `backend/src/modules/XiHan.BasicApp.Chat`：内部会话、消息、实时推送；Meta WhatsApp 接入应留在 Chat/Channel 边界，不放入 CRM Domain。
- `AI`、`Workflow`、`CodeGeneration`、`Printing`：独立业务模块。
- `backend/src/main/XiHan.BasicApp.WebHost`：模块宿主、配置、升级脚本和运行时接线。
- `backend/test`：10 个测试项目；其中 `Api.Tests` 承担较多跨模块契约/集成测试。

当前树统计：约 4,526 个路径、20 个 .NET 项目文件、10 个测试项目、2,299 个 C# 文件。Saas 模块约 1,379 个文件，CRM 约 236 个文件，Chat 约 43 个文件。Saas 规模远大于 CRM 是平台底座积累的结果，不应为了“平均”而机械拆分。

### 前端

- `frontend/src/api/modules`：类型化 API facade 与 DTO 类型。
- `frontend/src/modules/crm`：CRM 页面、语言和模块注册。
- `frontend/src/modules/chat`：Chat 页面、API、语言和 setup。
- `frontend/src/views`：平台级页面。
- `frontend/packages`：跨页面共享组件、request、router、stores、utils、XiHan.UI 集成。
- CRM 的 API 文件位于 `src/api/modules/crm`，页面位于 `src/modules/crm/views`；该分离与仓库约定一致，不应再复制一套 API 客户端或页面路由。

## 3. 关键业务逻辑链

### 自动获客主链

```text
Campaign / Strategy
        ↓
Run 创建与不可变 Provider Plan Snapshot
        ↓
Run Queue → Run Lease / Fencing / Heartbeat
        ↓
Provider Registry → Provider Request Ledger
        ↓
Provider I/O → Checkpoint / Recovery / NeedsReview
        ↓
Candidate Factory / Identity Resolver
        ↓
Candidate + Evidence + Score 原子持久化
        ↓
Coverage / Cursor 最后推进
        ↓
Run 汇总、取消或终态
        ↓
人工审核 → 既有 CRM Lead/Customer 转化
```

原则：Candidate 是待审核线索，不等同于正式 Lead；Provider 结果必须先持久化证据/评分，之后才能推进覆盖游标；租约、token、fencing version 和数据库时间共同决定写入权。异步 Provider 必须由独立 Extraction Worker 和父 Run continuation 闭环承接，不能塞进当前同步 Run Worker 的 foreach 中。

### WhatsApp Meta 主链（目标状态）

```text
Meta GET challenge / POST webhook
        ↓
原始请求体验签 + 有界读取 + 严格 JSON 解析
        ↓
WABA + phone_number_id 服务端账号绑定
        ↓
Tenant-scoped Inbox 原子插入 / EventKey 去重
        ↓
Inbox Consumer 租约、重试、NeedsReview
        ↓
Chat 消息 / 状态回执投影
```

当前代码只完成验签、解析、EventKey 和 Inbox schema 基础；租户绑定、Inbox repository、consumer/projection 尚未完成。未可靠落库前返回 HTTP 503 是正确的 fail-closed 行为。

### CSV 导入主链（当前仅预览）

```text
上传 CSV
  ↓
UTF-8 / 大小 / 行列 / 引号与换行校验
  ↓
服务器字段 allowlist + mapping + required/validator
  ↓
只读预览与错误统计
  ↓
（尚未实现）不可变预览快照
  ↓
（尚未实现）人工确认 + 幂等事务批量写入 + 回滚/审计
```

当前 Preview API 不写 CRM 数据，不能称为“导入功能已完成”。Contact 的 CustomerId 当前只验证为正整数，尚未验证该客户存在且属于当前租户；真正写入前必须增加批量、租户范围的客户存在性检查，不能逐行查询。

## 4. 复杂度热点与重构建议

### P0：Run Worker 上帝方法

文件：`CrmProspectingRunHostedService.cs`

- 文件约 87 KB / 1,452 行。
- `ProcessItemAsync` 约 946 行，是当前最大复杂度热点。
- 一个方法同时承担 Run 租约、不可变计划校验、Provider 执行/异常分类、checkpoint 重放、请求账本恢复、原始记录限额、候选规范化、身份补全、Evidence/Score 修复、覆盖提交、取消竞争和 Run 终态。

**优化方向（先加行为特征测试，再逐步提取，不一次性重写）：**

1. `CrmProspectingRunCoordinator`：消息校验、租户 scope、Run lease、heartbeat、恢复和取消。
2. `CrmProspectingProviderExecutionService`：Provider preflight、ledger、cooldown、checkpoint、外部调用和 provider outcome。
3. `CrmProspectingCandidateIngestionService`：记录验证、Candidate Factory、Evidence/Score 原子持久化、重复身份修复。
4. `CrmProspectingCoverageFinalizer`：覆盖/游标推进与 Run 终态；确保始终晚于持久化。
5. 把 Provider 错误、NeedsReview、Cancelled、Retryable、Completed 归一为明确 outcome，不用跨层异常字符串作为状态协议。

拆分时保留单一事务所有权：候选/Evidence/Score/游标必须共享既有事务边界；不要让每个新服务各自开启事务，也不要把当前 lease/fencing 参数丢失到不透明的 helper 中。

### P1：CRM 服务注册入口过度集中

文件：`Crm/Extensions/ServiceCollectionExtensions.cs`

- 约 24.8 KB / 415 行。
- 当前一个 `AddCrmDataSeeders` 入口注册仓储、Provider Registry、Candidate Persistence、领域服务、几十个 opt-in Provider、权限种子；PR #5 又把多个 ExportProvider 注册加入同一文件。
- 方法名“DataSeeders”不能准确表达其真实职责，且让注册差异难以审阅。

**优化方向：** 拆成 `AddCrmRepositories`、`AddCrmDomainServices`、`AddCrmProspectingProviders(configuration)`、`AddCrmExportProviders`、`AddCrmDataSeeders`。每个 Provider 的 host、认证、timeout、tenant scope 仍显式声明；不要引入隐藏配置约定的反射式注册器。

### P1：测试职责和断言方式

- PR #3 新增的 PostgreSQL 集成测试约 890 行、15 个用例；建议按 Claim/TenantIsolation、SubmissionIdempotency、LeaseFencing、CursorAtomicity 分文件，公共 fixture 统一建表/租户 scope/清理。
- `CrmProspectingExtractionRepositoryContractTests` 与已有 `CrmProspectingExtractionPersistenceContractTests` 都有源码字符串断言。源码断言适合保护少数 SQL 安全不变量，但不应替代行为测试，也不应重复验证同一 SQL 片段。
- 优先级：真实 PostgreSQL 行为测试 > 公开接口契约测试 > 少量必要的 source guard。减少依赖方法名、源码片段和行序的脆弱测试。
- PR #3 的双事务 cursor race 目前使用短延时同步；应改用数据库锁/屏障信号，避免时间假设。

### P1：Provider 适配器

不同 Provider 的分页、认证、速率限制、响应形态和权威等级不同。保留统一的 Provider contract、registry、request ledger、HTTP endpoint guard 和 sanitizer；共享真正一致的 transport/policy。不要为了减少文件数量而把不同数据源塞进一个巨大 switch，也不要用继承隐藏 Provider 特有行为。

### P2：CSV 与导入

- PR #5 的 Lead/Customer/Contact Preview 共用一个有界 CSV parser，这是正确复用。
- 7 个 CRM ExportProvider 是通用 `QueryServiceExportProviderBase` 的薄适配器，主要是业务类型、权限和 QueryService 绑定；这是必要接线，不是应删除的重复逻辑。
- 前端 CSV 工具与后端导入 parser 不应合并：前端处理体验，后端承担安全校验和信任边界。
- 不要在 Preview 阶段写 CRM；正式导入需要 preview snapshot hash、mapping snapshot、用户确认、tenant scoped validation、幂等键、批量事务和失败回滚。

## 5. 文档去重规则

当前 `docs/kiger` 已有约 36 份专项文档。迁移审计内容有重复增长：基础分支已有 `HAITUO-CRM-FUNCTION-MIGRATION-MATRIX.md` 与 `HAITUO-CRM-FUNCTION-PARITY-AUDIT-2026-11-06.md`；PR #5 又增加 Source Inventory、Target Reuse Audit、Second Pass Audit、Gap Register 四份文档，内容存在明显交叉。

建议固定三种文档职责：

1. **Migration Matrix**：只回答“源能力 → 目标入口 → 语义差异”。
2. **Parity Audit**：只回答“已实现证据、未实现证据、测试/运行验收状态”。
3. **Gap Register**：只维护优先级、负责人/分线、验收条件和当前阻塞。

Source Inventory、Target Reuse、Second Pass 的独有事实应并入上述三份文档后再删除重复快照。自动获客的 lease、coverage、identity 等专题设计文档保留，因为它们是具体技术决策，不与迁移矩阵同一职责。每个审计文档必须写明基准分支、commit SHA、检查日期和验证范围，过期快照标记为 superseded，不再继续复制新版本。

## 6. 分支与集成风险

- 当前四个工作 PR 均以 `kiger-dev` 为 base，且各自与该 base 无 behind；这是本轮工作的集成基线。
- 仓库 `main` 与 `kiger-dev` 的 compare 显示 diverged（`kiger-dev` 相对 main 约 ahead 2,628、behind 6）。这不是普通的小型分支落后；不得把本轮工作直接改基线或合并到 `main`。先由仓库维护者确定主干收敛策略。
- PR #2/#3/#4/#5 当前仍为 Open/Draft。各 PR 提交历史仍较多且粒度偏细，会让审阅历史噪声上升。验收前不要强行重写远程历史；达到可合并状态后再按仓库策略 squash。
- 当前 PR #4 的 5.9.41 SQL 和迁移测试源码曾出现换行损坏，已在 PR 分支修复；仍需真实编译和 PostgreSQL migration test 验证。

## 7. 本轮确认的功能状态

| 领域 | 代码现状 | 不能宣称 |
|---|---|---|
| CRM 主数据 | 既有 Customer/Contact/Lead/Opportunity/Quote/Order/Shipment 模块 | 不代表与源系统所有行为逐项等价 |
| 自动获客 | 同步 Run、Provider、Candidate/Evidence/Score、租约/账本基础存在 | 异步 Submit/Poll/Fetch + Parent continuation 尚未完成 |
| PostgreSQL | 有 SQL lease/fencing/cursor 设计和测试 | 未运行真实 PostgreSQL 测试不能称通过 |
| WhatsApp Meta | 验签、解析、event key、Inbox schema/migrations 基础存在 | 未有 tenant routing、durable writer、consumer 时不能返回成功 |
| CRM CSV | 只读预览、映射和基础字段校验 | 不是真正的批量导入 |
| CRM Export | 可复用平台异步导出设施，新增 CRM providers | 仍需确认字段、筛选、权限和实际导出验收 |
| 前端 | 现有 CRM/Chat 页面和类型化 API 分层存在 | PR #4/#5 的新接入没有形成完整 UI/端到端验收 |

## 8. 推荐执行顺序

1. **先稳住基线**：所有 PR 保持 Draft；修复已发现的编译/SQL 问题；先获得可重复的 build/test 结果。
2. **拆分 Run Worker**：先为 Provider 执行、候选持久化、coverage/finalization 建立行为测试，再逐块提取。
3. **拆分 DI 注册**：将仓储、领域服务、Provider、Export 和 Seeder 注册分开，保持注册数量/生命周期完全一致。
4. **收敛测试**：拆分 PostgreSQL fixture 和测试文件，删除重复 source guard，补齐真实事务与租户隔离测试。
5. **收敛文档**：将迁移审计统一到 Matrix / Parity / Gap Register，停止产生同内容的新审计快照。
6. **再做功能闭环**：异步 Extraction、WhatsApp Inbox、CSV confirmed import 均按完整事务和恢复路径实现；不以 DTO/枚举/文档代替运行时工作流。
7. **最终 Windows 验收**：用户机器拉取 `kiger-dev` 的已整合版本，执行 .NET restore/build/test、PostgreSQL migration/integration、前端 check/test/build 和核心业务实机验收。

**重构纪律：** 每次只收敛一个边界；不为了抽象而抽象；不复制 CRM 主数据；不扩大权限；不提前确认外部发送或网站发布；不以静态测试替代真实运行证据。

 
## 9. 已执行的第一轮结构收敛（2026-11-08）

在 `work/d-migration-audit` 分支已完成一轮行为保持型 DI 注册拆分：

- 原 `Crm/Extensions/ServiceCollectionExtensions.cs` 将实体字段安全、ExportProvider、仓储、Provider Registry、领域服务、26 个 opt-in Provider 和权限 Seeder 放在同一注册方法中，原文件约 425 行。
- 现在核心 `ServiceCollectionExtensions.cs` 约 104 行，职责拆为 `AddCrmServices`、`AddCrmRepositories`、`AddCrmDomainServices`、`AddCrmDataSeeders`。
- 新增 `CrmProspectingProviderRegistrationExtensions.cs`，集中保留 26 个显式配置开关和 Provider 专属 endpoint/auth/timeout/lifetime。
- 新增 `CrmExportProviderRegistrationExtensions.cs`，单独注册 7 个 CRM ExportProvider。
- CRM Module 的 ConfigureServices 现在按服务注册与 Seeder 注册两个步骤接线。

这轮只移动和命名既有注册逻辑，没有更改 Provider 的配置键、生命周期、endpoint、timeout 或注册接口。由于当前执行环境缺少 .NET SDK，尚未编译；后续必须用服务注册测试和完整解决方案 build 验证，再考虑继续拆 Provider 分类。


## 10. DI 注册复核与回归保护（2026-11-08）

对 `kiger-dev` 原始注册实现与 `work/d-migration-audit` 拆分后的实现进行了逐个配置块比较：

- 26 个 `XiHan:CRM:Prospecting:Enable*` 配置键全部保留，无缺失、无新增。
- 26 个 Provider/identity-resolver 条件注册代码块逐块比较后内容一致，Provider endpoint、认证读取方式、timeout、服务生命周期和接口映射未发现变化。
- 7 个 CRM `IExportProvider` 注册仍全部为 Scoped。
- 新增 `CrmServiceRegistrationTests`，保护 7 个导出 Provider、默认关闭 prospecting provider、全部 25 个 prospecting provider 注册、GLEIF identity resolver 和 Scoped Provider Registry 的注册边界。

验证边界：这是源码逐块比对和新增测试代码；当前没有对应 GitHub Actions workflow run，也没有 .NET SDK 执行测试。因此不能把该测试标记为已运行或通过。后续 Windows/.NET 环境应先运行该测试，再运行 CRM/API 测试项目。


## 11. PostgreSQL 游标并发测试同步优化（2026-11-08）

在 `work/b-postgres-regression` 的双独立事务 cursor CAS 测试中，移除了固定 `Task.Delay(100)` 的调度假设：

- 新增第三个独立 observer SQL client。
- 第一个事务先执行 cursor CAS 并持有目标行锁；第二个事务发起竞争更新。
- observer 轮询 PostgreSQL `pg_stat_activity`，只有在观察到针对 `Crm_Prospecting_Extraction_Job` 的活动查询确实处于 Lock wait 时，才释放第一个事务。
- 增加超时失败路径的竞争任务收尾，避免回滚后竞争写仍在后台运行并与测试清理竞争。

这比固定延时更接近真实数据库并发条件，但仍需真实 PostgreSQL 验证：确认测试数据库用户能观察同用户会话的 query/wait_event，确认 SqlSugar 异步查询 API 与 PostgreSQL 生成 SQL 符合预期。测试尚未执行，不标记为通过。


## 2026-11-08 B 线并发测试隔离补充

- PostgreSQL cursor CAS 集成测试的 lock-wait observer 现在先从竞争事务连接读取 `pg_backend_pid()`，再只观察该 PID 在 `pg_stat_activity` 中是否处于 `wait_event_type = 'Lock'`、`state = 'active'`，且当前 SQL 命中 extraction job 表。
- 这比仅按表名匹配更稳健：并行运行的其他集成测试即使同时更新同一张表，也不能误触发本测试释放首个事务。
- 本改动已提交到 `work/b-postgres-regression`（`24eb12abf38bfa9f2f8e03f44d98d5beb9a8bc82`），PR #3 仍为 Draft。
- 仍需真实 PostgreSQL 环境验证 backend PID 可见性、活动查询文本匹配和竞争 UPDATE 的锁等待行为；本次没有执行数据库测试或 .NET 编译。


## 2026-11-08 follow-up: PostgreSQL cursor-race cleanup

The B-line cursor compare-and-swap integration test now observes the exact competing backend PID in `pg_stat_activity` and waits for PostgreSQL to report a lock wait before releasing the first transaction. A follow-up review found that a bounded wait while draining the competing task could still allow fixture deletion to race a background SQL operation if that wait timed out. The failure path now rolls back the first transaction and awaits the competing task to exit before the fixture cleanup runs. This is a test-lifecycle hardening change only; it does not alter production repository behavior.

**Verification remains pending:** no .NET SDK or configured PostgreSQL test instance is available in this environment. The real PostgreSQL run must verify `pg_stat_activity` visibility and the actual generated UPDATE query text.


## 2026-11-08 follow-up: WhatsApp legacy idempotency compatibility

Review of the 5.9.40 backfill against the new event-key factory identified a replay-compatibility boundary: old inbound rows carry the migration's MD5 key (`tenant_id:WABA:wamid`), whereas new inbound keys are SHA-256 over a length-prefixed tuple that also includes phone_number_id. The factory now exposes a compatibility helper that exactly reproduces the legacy MD5 key, with a regression test. The future Inbox writer must check both legacy and new keys within the resolved tenant/WABA scope before insert; concurrency-safe persistence and PostgreSQL replay tests remain unimplemented. Webhook POST remains 503 until that path is complete.


### WhatsApp legacy-key exact-byte follow-up
The compatibility helper was further hardened after comparing it byte-for-byte with migration 5.9.40: SQL hashes the stored WABA/message ID values verbatim, so the legacy lookup must not trim non-empty values. It now rejects blank identities while preserving surrounding whitespace, with a regression test. This is distinct from the new SHA-256 event-key normalization and is required for exact replay compatibility with historical rows.


### WhatsApp status-key test fixture review
The status-key tests contained over-escaped JSON-array strings for error payloads. Those fixtures were invalid JSON and would fail in `JsonDocument.Parse` before exercising event-key canonicalization. The fixture escaping was corrected; no runtime implementation behavior changed. .NET test execution is still pending.


## 2026-11-08 follow-up: prospecting Run lease-recovery state-machine review

A focused source review of `CrmProspectingRunRepository` and `CrmProspectingRunHostedService.RecoverPendingAsync` confirmed these current invariants:

- `TryClaimLeaseAsync` claims only Queued rows or Running rows with a non-null lease expiry at/before PostgreSQL `clock_timestamp()`; legacy Running rows with no lease expiry are not silently taken over.
- Claim generates a fresh lease token and increments the fencing version in the same PostgreSQL UPDATE that returns the claimed row.
- `RecoverExpiredLeasesAsync` requeues expired Running rows, but converts expired CancelRequested rows to Cancelled; it clears lease metadata using database time.
- `RecoverInterruptedAsync` is restricted to rows with all lease metadata null. It requeues legacy Running rows and preserves cancellation intent by finalizing legacy CancelRequested rows as Cancelled.
- The hosted service runs recovery inside the tenant data-scope runner, ensures request-ledger constraints first, and only enqueues queued IDs under the explicit tenant context.

No parent/child extraction continuation state exists yet. Therefore this review does **not** authorize adding WaitingForExtractions or treating acknowledged extraction jobs as completed parent work. The next A-line implementation still needs a single transaction boundary for child submission intent plus parent waiting transition, and a fenced continuation/recovery path before any async Provider can be enabled.

### Remaining verification gap

The source-level state transitions are coherent, but no .NET tests or PostgreSQL integration tests were run in this environment. The next A-line regression work should add database-backed coverage for: (1) expired Running takeover increments fencing and invalidates the old token; (2) expired CancelRequested becomes Cancelled and is never requeued; (3) legacy Running with null lease metadata follows only the explicit legacy recovery path; and (4) two concurrent claimers yield one lease owner. These are test targets, not claimed passing results.


## 2026-11-08 parallel follow-up: A/C/D regression hardening

- **A / async prospecting:** expanded the design contract with a parent continuation crash-window matrix covering pre-transaction, child-intent rollback, post-wait/pre-enqueue, remote-submit ambiguous timeout, child-terminal/pre-signal, continuation finalization, and cancellation races. It remains design-only; no WaitingForExtractions state or parent coordinator has been implemented.
- **C / WhatsApp:** added explicit regression coverage that the legacy migration-key helper rejects whitespace-only WABA/message IDs while retaining exact bytes for non-empty stored values. This does not implement Inbox lookup or persistence; POST remains 503.
- **D / CSV contact preview:** added CustomerId format boundary cases for positive Int64, zero, negative, explicit plus, decimal, and overflow. This does not prove tenant-scoped customer existence; bounded batch lookup remains required before import.
- **B / PostgreSQL:** re-reviewed the cursor CAS lock-wait test. It captures the competing backend PID, observes that exact session in pg_stat_activity, filters for the extraction table UPDATE, rolls back on failure, and drains the competing task before fixture cleanup.
- Tests remain unexecuted. The current environment still has no .NET SDK / configured PostgreSQL validation evidence; no runtime pass is claimed.

- **D / fixture review:** corrected the newly added CustomerId boundary test fixture to emit actual CSV record newlines rather than literal backslash-n characters. The source was re-fetched and statically checked after the correction; execution remains pending.


## 2026-11-08 D-line implementation follow-up: tenant-scoped Contact reference validation

- Added a read-only customer lookup contract and implementation over the existing SaasRepository<SysCrmCustomer>, preserving tenant and soft-delete query filters.
- IDs are distinct-filtered, queried in batches of 500, and capped at 5,000 per preview request.
- Contact preview validates the complete bounded row set (not only the 100 rows displayed) so aggregate counts include missing tenant customer references beyond the visible sample.
- API response remains capped at 100 preview rows. Missing IDs produce a row-level error; no CRM data is written.
- Added service tests for missing customer references and full-file count correctness. Not executed in this environment.
- Remaining D-line work: immutable preview snapshot, human confirmation, idempotent transactional import/rollback, and export behavior acceptance.


## 2026-11-08 C-line module registration correction

- Source review found the WhatsApp Inbox entity and migrations existed, but Chat module setup did not register the Inbox entity through the platform field-security/entity metadata extension.
- Added Chat entity registration and invoked it from Chat module ConfigureServices.
- Extended the source contract test to assert the Inbox entity is part of that registration.
- This is metadata/composition-root wiring only. No tenant account binding, Inbox writer, deduplication transaction, or consumer was added; webhook POST remains 503. Tests remain unexecuted.


## 2026-11-08 parallel follow-up: PostgreSQL-backed lease and tenant lookup coverage

- **A / Run lease state machine:** added PostgreSQL-gated tests using two independent clients for concurrent claim arbitration, expired lease takeover with token rotation/fencing increment, stale-owner renewal rejection, expired CancelRequested recovery to Cancelled without requeue, and explicit legacy lease-less Running recovery.
- **D / Contact CustomerId lookup:** added PostgreSQL-gated repository tests proving tenant isolation, deduplication, and retrieval across the 500-ID batch boundary with 501 IDs.
- Both test groups require KIGER_TEST_POSTGRES_CONNECTION_STRING. They have not been executed; no compile or runtime pass is claimed.
- Parent/child extraction continuation, WhatsApp WABA/phone tenant routing and durable Inbox persistence, and confirmed transactional CSV import remain incomplete.


### B / PostgreSQL observer query hardening

The cursor-CAS race test now binds the competing PostgreSQL backend PID as a query parameter when polling pg_stat_activity, rather than interpolating it into SQL. The test comment records the observer-role visibility requirement: same-role session visibility is expected; deployments using a different observer role need pg_read_all_stats. This is test synchronization hardening only. PostgreSQL execution remains pending.


### D / PostgreSQL fixture cleanup hardening

Customer lookup integration fixtures now register generated IDs before executing batch inserts and clean up partially created rows in finally blocks, including the case where the second tenant's fixture insertion fails. This improves failure isolation and avoids contaminating subsequent PostgreSQL integration runs. Execution remains pending.


### A / lease integration schema-name contract review (follow-up)

Static review found a schema-name contract that must be resolved before the new PostgreSQL lease tests can be treated as executable evidence:

- `SysCrmProspectingRun` declares `[SugarTable(TableName = "Crm_Prospecting_Run")]`.
- The lease repository's raw PostgreSQL statements address `crm_prospecting_run` and snake-case lowercase columns.
- The integration fixture calls `CodeFirst.InitTables(typeof(SysCrmProspectingRun))` before invoking those raw statements.
- Confirm the actual migration / SqlSugar PostgreSQL identifier mapping and align the fixture plus raw SQL with the one canonical physical table/column contract. Do not mask the mismatch by creating a second table or claiming the tests pass.

This is a static finding only; the environment still lacks .NET/PostgreSQL execution evidence. Parent/child continuation remains unimplemented.


### Correction: A / PostgreSQL identifier folding review

A follow-up review corrected the preceding static finding: PostgreSQL folds unquoted identifiers to lowercase. Therefore the SqlSugar table name `Crm_Prospecting_Run` (when emitted unquoted) resolves to `crm_prospecting_run`; explicitly mapped columns such as `Campaign_Id` similarly resolve to lowercase `campaign_id`. The entity/table spelling and repository's unquoted lowercase SQL are not, by themselves, a physical-name mismatch. The prior note is superseded and must not be treated as a confirmed defect. Runtime verification is still pending for the actual CodeFirst/migration schema, inherited base columns, and SqlSugar RETURNING-to-entity mapping.


### Correction: C / Chat Inbox field-security catalog registration

The earlier attempt to register `SysWhatsAppWebhookInbox` through `AddFieldSecurityEntities` was not a valid database entity-discovery mechanism. The WhatsApp migration design already distinguishes field-security metadata from entity/table discovery. Removed that registration and its source-contract assertion to avoid broadening the field-security catalog incorrectly. This does not remove the existing Inbox entity or migration. New-database CodeFirst discovery still needs verification against the framework's actual entity initialization path; do not infer it from field-security registration.


### A / Run lease write-fencing takeover regression

Added a PostgreSQL-gated integration test that claims a Run lease, expires and takes it over through a second independent client, then exercises `TryLockLeaseForWriteAsync` inside a transaction. The superseded token/fencing version must be rejected while the replacement live lease must be accepted. This closes a gap between renewal/claim coverage and the transactional write-lock gate. The test requires `KIGER_TEST_POSTGRES_CONNECTION_STRING` and has not been executed; no build or PostgreSQL pass is claimed.
