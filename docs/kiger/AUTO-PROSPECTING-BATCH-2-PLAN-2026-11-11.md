# 自动获客迁移批次 2 计划 — 执行监控与候选批量操作闭环

> 状态：**计划，未实施**。基线：`kiger-dev` `9f0b55a03`（2026-11-11 异步 Extraction Worker 合入后）。
> 依据：2026-11-11 与源项目 `hongshuoguo2-cloud/haituo-crm`（`D:\crm`）的自动获客功能/界面操作差距矩阵；`HAITUO-CRM-MIGRATION-GAP-REGISTER.md`（关闭规则）；`PARALLEL-PROSPECTING-WORKSTREAMS-2026-11-06.md`（工作流格式与 B/E 切片）。
> 本批主题：**看得见（Run 监控）、批得动（候选批量操作）**。

## 1. 本批覆盖的差距

| 差距项（2026-11-11 矩阵） | 等级 | 本批动作 |
|---|---|---|
| Run 进度：源有分阶段+耗时，目标仅状态+5 个计数 | P0 | W1 进度聚合 + 台账 UI |
| 实时性：源有 SSE，目标仅手动刷新 | P0 | W1 有界轮询（SSE 另行评估） |
| Run 控制：源有暂停/恢复/取消，目标仅取消 | P0 | W4 暂停/恢复（可顺延） |
| 候选列表：无分页、无导出、无批量选择 | P0 | W2 分页+导出、W3 批量 |
| 批量与短名单：源有短名单+批量转 Lead，目标仅单条 | P0 | W3 短名单 + 批量审核/转换 |
| 来源执行台账：源有来源级成功/失败/真实错误 | P1 | W1 顺带关闭（数据已存在，见 §3.1） |
| 超级搜索、自然语言解析、定期计划、资格四步、终态报告 | P0/P1 | **不在本批**，见 §6 批次路线 |

优先级等级不等于同批执行：本批选的是"持久化数据已存在、以查询与界面为主"的项，先闭合操作体验；搜索能力扩展（超级搜索/计划）依赖新管线接线，排批次 3。

## 2. 共同约束（每个工作流都适用）

1. 租户隔离：所有查询走 `ISaasRepository`/现有仓储的当前租户与软删过滤；跨租户不可读写，测试覆盖。
2. 权限后端强制：新写操作标 `[PermissionAuthorize(...)]`，动作码进 `CrmPermissionCatalogSeeder` 种子；前端隐藏按钮不能替代后端授权。
3. 写操作沿用 `[UnitOfWork(true)]`；异步方法接受并传递 `CancellationToken`；不吞异常、不返回伪成功、无静默 fallback。
4. 不搞双轨协议：改签名的接口与前端调用点同提交一次收口，不留旧的固定 500 接口与兼容垫片。
5. 前端只用 XiHan.UI 与语义令牌；新文案进 `frontend/src/locales/langs/*` 全语言键（i18n 门禁）；不新增裸 px。
6. 验收按 `HAITUO-CRM-MIGRATION-GAP-REGISTER.md` 关闭规则 5 条（源端定位、目标端定位、正常/异常/权限/并发/恢复路径测试、自动化测试、Windows 实机验收）；静态代码存在不算关闭。
7. 每个工作流独立提交；同一契约（DTO+API+权限+种子+i18n+测试）放同一提交。改 DTO/权限码/菜单/`UpdateScripts` 前先确认与并行工作线（PARALLEL 文档 B/E）无冲突。
8. 数据库结构变更放 WebHost `UpdateScripts/<下一版本>/<下一版本>.sql`，PostgreSQL、可重复执行；由应用启动自动执行，Agent 不手工对任何库执行脚本。

## 3. 工作流

### W1 — Run 监控：进度聚合 + 分来源执行台账 + 自动刷新（P0，顺带关 P1 台账）

**Current verified boundary（2026-11-11 核对）**

- 台账数据**已持久化**：`SysCrmProspectingRunProviderExecution`（`Crm_Prospecting_Run_Provider_Execution`）按 plan ordinal 存每 provider 检查点：`State`（Queued/Started/Checkpointed/Projecting/Succeeded/Failed/NeedsReview/Skipped）、`AttemptCount`、`RawRecordCount`、`ProjectedRecordCount`、`FailureCount`、`FailureCode`/`FailureSummary`、`StartedAt`/`CompletedAt`；仓储 `ICrmProspectingRunProviderExecutionRepository` 已注册（`Extensions/ServiceCollectionExtensions.cs:96`）。`SysCrmProspectingProviderRequest` 是 HTTP 尝试级台账（冷却、覆盖检查点），本批不暴露明细。
- `Run` DTO 只有状态与 raw/candidate/failure 计数；`GetRunCandidatesAsync` 等全部列表接口固定 `Take(500)`；无任何进度查询 API。
- 前端 `crm/` 目录下无轮询；但仓库已有可复用的轮询范式：`frontend/src/views/file/export-center/index.vue:26-47`（5s `setInterval` + `document.visibilityState === 'visible'` 才刷新 + `onBeforeUnmount` 清理）。

**Implementation slices**

1. 后端新增只读查询（QueryServices 约定，`[PermissionAuthorize(CrmPermissionCodes.Read)]`）：
   - `RunProviderExecutions(runId)`：按 `PlanOrdinal` 返回台账行（provider code、state、attempt、raw/projected、failure code/summary、起止时间）。
   - `RunProgress(runId)` 或并入现有 `Run` DTO：按 `State` 聚合的进度结构（queued/running/terminal 计数、完成百分比）。**完成口径在实现时定稿并写测试**：终态含 Succeeded/Failed/Skipped；NeedsReview 计入终态但单独标记"待人工恢复"。
2. 前端 Run 区展开详情（新组件，见 W5）：进度条 + 台账表；失败行展示 `FailureCode`/`FailureSummary`；NeedsReview 行给人工提示样式。
3. 自动刷新：仅当 `status ∈ {Queued, Running, WaitingForExtractions}` 时按 export-center 范式 5s 轮询 Run+进度+台账；终态停止、页面隐藏暂停、卸载清理；保留手动刷新按钮。
4. i18n 全语言键；台账表在窄视口下横向滚动（`oc-table` 类似物按 XiHan.UI 现有表格能力）。

**Acceptance gates**

- 查询租户隔离测试（他租户 runId 返回空/拒绝）；进度聚合口径测试（空 plan、部分失败、NeedsReview、WaitingForExtractions）。
- Vitest：轮询生命周期（终态停止、隐藏暂停、卸载清理、手动刷新不重复起定时器）。
- 浏览器实机：亮/暗色、窄视口、键盘焦点、reduced motion；`pnpm check`、`pnpm test`、`pnpm build`。
- 后端所属模块测试 + 解决方案 `dotnet build`/`dotnet test`。

### W2 — 候选列表分页 + 导出（P0）

**Current verified boundary**

- `CrmProspectingAppService.GetRunCandidatesAsync`（`Application/AppServices/CrmProspectingAppService.cs:350-361`）固定 `Take(500)`，无分页 DTO；campaigns/strategies/runs 同为 500（本批只收口候选，其余按需顺延）。
- 分页既有模式：`Application/QueryServices/*` 的 `GetPageAsync(PageQueryDto)` + `PageResultDtoBase<T>`（如 `CrmTaskQueryService`）。
- 导出既有机制：后端 `Application/Exporting/` 的 `QueryServiceExportProviderBase<TQuery,TItem>` + `BusinessType`（CRM 口径 `crm.lead`/`crm.contact`…）+ `CrmExportProviderRegistrationExtensions`；前端统一"导出中心" `frontend/src/views/file/export-center/index.vue`（任务式导出，`businessTypeOptions` 是展示副本，**新增 Provider 需同步补一条**并加 `file.export_center.business_type.*` 文案，见该文件 L67-77 注释）。
- 导出权限既有模式：`CrmPermissionCatalogSeeder` 中 `crm_customers.export`（`CrmPermissionCodes.Execute`）。

**Implementation slices**

1. 新建 `CrmProspectingCandidateQueryService.GetPageAsync(runId, status, page, …)`：`PageResultDtoBase<CrmProspectingCandidateListItemDto>`，稳定排序（如 `Id` 兜底），软删与租户过滤走仓储。
2. 一次收口：移除固定 500 的 `RunCandidates` 形态，前端同一提交切换到分页查询；不留旧接口。
3. 导出：`CrmProspectingCandidateExportProvider`（`BusinessType => "crm.prospecting.candidate"`，TQuery 复用分页查询 DTO），注册到 `CrmExportProviderRegistrationExtensions`；种子 `crm_prospecting.export`（Execute，挂在 `crm_prospecting` 菜单动作下）。
4. 前端：候选表分页控件（复用 `createPageRequest` 模式）；"导出"按钮按当前筛选创建导出任务（走 export-task 通用 API），权限门控；导出中心 `businessTypeOptions` 补条目与 i18n。

**Acceptance gates**

- 分页正确性：跨页不重不漏（含软删边界）、状态筛选×分页组合、排序稳定；租户隔离测试。
- 导出：列契约与筛选一致性（runId/status 生效）、越权拒绝路径、超限口径（导出行数上限按导出中心现有约定）。
- 种子/菜单/权限三者一致的源契约测试；前端门禁 + 浏览器实机检查。

### W3 — 批量审核、批量转 Lead 与短名单（P0）

**Current verified boundary**

- 候选状态机：`CrmProspectingCandidateStatus` = PendingReview/Duplicate/Approved/Rejected/Converting/Converted；现有单条 `ReviewCandidate`、`ConvertApprovedCandidateToLead`；转 Lead 的人工可联系门槛在后端。
- 候选实体无短名单字段；无任何批量 API；前端无多选。

**Implementation slices**

1. 实体：`SysCrmProspectingCandidate` 增加短名单标记（建议 `ShortlistedAt` 可空时间 + 必要索引），`UpdateScripts/<下一版本>/<下一版本>.sql` 可重复执行；实体/迁移源契约测试。本批范围=Run 候选列表内筛选；跨 Run 的独立短名单页属后续批次。
2. 批量 API（`[UnitOfWork(true)]` + 权限标注）：
   - `BulkReview(ids, approve)`：逐项校验状态机前置；
   - `BulkConvert(ids)`：逐项走既有转 Lead 门槛；**部分成功语义**：逐项如实返回成功/失败+原因，单项失败不污染其它项（事务边界实现定稿：逐项独立提交或保存点），已 Converted 的幂等跳过并报告，禁止吞异常伪造全成功；
   - `ShortlistCandidates(ids)` / `UnshortlistCandidates(ids)`（或一个 toggle）。
3. 前端：候选表多选 checkbox + 批量动作条（批准/拒绝/转 Lead/加入短名单）；短名单筛选；批量结果汇总反馈（成功 n / 失败 m + 逐项原因可展开）。
4. 权限：复用 prospecting 更新/执行口径；如需新动作码走种子。

**Acceptance gates**

- 批量部分失败语义测试（3 成功 1 失败的状态与返回体）；幂等重放；并发（两会话同时转同一候选，PG 门控集成测试 `KIGER_TEST_POSTGRES_CONNECTION_STRING`）；租户越权；权限拒绝。
- 状态机全覆盖：PendingReview→Approved→…→Converted 各路径 + 非法跃迁拒绝。
- 前端门禁 + 浏览器实机（多选焦点、批量条键盘可达）。

### W4 — Run 暂停/恢复（P0，风险项，可顺延至批次 3）

**Current verified boundary**

- `CrmProspectingRunStatus` = Queued/Running/Succeeded/PartiallySucceeded/Failed/CancelRequested/Cancelled/WaitingForExtractions（`Domain/Entities/Enums/CrmProspectingRunStatus.cs`），**无 Paused**；`Status` 以 int 落库（`SysCrmProspectingRun.cs:51-52`），追加枚举值预期无 schema 变更（实现时核对无 CHECK 约束）。
- 执行侧：`Infrastructure/Prospecting/CrmProspectingRunHostedService.cs`（同步派发 + 台账写入 + 租约恢复）与今日合入的异步 Extraction Worker；两者均有租约/fencing 语义。

**Implementation slices（先设计定稿，再动状态机）**

1. 语义文档（写入本节 Delivery status 或独立短文档）：
   - `PauseRun`：Running/Queued/WaitingForExtractions → Paused；停止**新** provider 派发；in-flight 调用允许完成并按现有台账路径落盘；已提交的异步子任务独立租约继续。
   - `ResumeRun`：Paused → 依台账推导回 Running 或 WaitingForExtractions（存在未完成子任务时）。
   - 优先级：暂停中收到取消 → 走既有 CancelRequested/Cancelled 路径，取消优先。
   - 恢复/租约过期路径：过期租约恢复逻辑不得把 Paused 的 Run 复活为 Running。
2. 枚举追加 + 状态机转换测试（仓储/域层）；`PauseRun`/`ResumeRun` Dynamic API + 权限码种子。
3. `CrmProspectingRunHostedService` 循环配合：派发前检查 Paused、恢复唤醒复用现有触发机制；异步父 Run 在 WaitingForExtractions 期间暂停的语义按第 1 条执行。
4. 前端：暂停/恢复按钮按状态显隐、Paused 状态展示与轮询（W1 的轮询集合纳入 Paused→不轮询或低频，定稿）。

**Acceptance gates**

- Worker 测试：暂停后不再派发、in-flight 完成落盘、恢复从台账断点续跑、暂停中取消、暂停中异步子任务语义、过期租约恢复不复活 Paused。
- 并发与 fencing 不回退（既有租约测试全绿）；PG 门控集成测试。
- **顺延规则**：若状态机风险超出预期（与异步 Worker 交互未定稿），W4 整体顺延批次 3，不阻塞 W1–W3/W5 的验收与登记。

### W5 — 页面结构收口（配套，随 W1–W4 持续进行）

- `frontend/src/modules/crm/views/crm/prospecting/index.vue` 现 965 行单页；每落地一个工作流拆出对应组件（RunDetailPanel、ProviderExecutionTable、CandidateTable、BulkActionBar 等），保持单页纵向结构与菜单/`Component` 路径不变（`PageRegistry` 的 `Crm/Prospecting/Index` 不动）。
- 保持现有约定：原生 confirm 局部豁免、XiHan.UI 组件、紧凑任务导向、无营销式大标题。
- 每次拆分后跑 `pnpm check`、`pnpm test`、`pnpm build` 与浏览器实机检查。

## 4. 集成顺序

```text
W1（监控基座，纯读、数据已存在）
 └─ W2（候选分页+导出） ∥ W3（批量+短名单，写路径）
     └─ W5（组件拆分，随每步进行）
         └─ W4（状态机，最后；可顺延，不阻塞前面验收）
```

- W1 先行的理由：W4 的暂停体验、W3 的批量结果反馈都依赖"看得见"；W1 只读、风险最低，先建立基座。
- W2/W3 相互独立（查询 vs 写路径），可并行不同会话/worktree；两者都动 `CrmProspecting` DTO 时按共同约束 7 先行沟通。

## 5. 验收与登记

- 每个工作流按 §2 第 6 条关闭规则取证；全部关闭后：
  1. 回写 `HAITUO-CRM-MIGRATION-GAP-REGISTER.md`（更新对应 P0/P1 行 + 增加"界面操作"维度状态）；
  2. 更新本文件 Delivery status；
  3. 差距矩阵中的对应行改判（监控/批量/台账/导出）。
- 运行时验收依赖本机预览 PG（`D:\kigercrm-preview`，5433）与 Windows 原生部署；不以测试源码存在代替执行。

## 6. 批次路线（本批不做）

| 批次 | 主题 | 内容 | 依据 |
|---|---|---|---|
| 批次 3 | 搜索能力扩展 | 超级搜索接线（`CrmProspectingSuperSearchPlanner` 等孤儿类，现无生产调用方）+ 自然语言目标解析 + 定期计划 Schedule + （W4 顺延项） | `PARALLEL-PROSPECTING-WORKSTREAMS` 工作流 B/E |
| 批次 4 | 资格与报告 | 资格四步（身份核验 GLEIF discover/reconcile、联系人补全、官网 probe）+ 终态/无结果报告 | 差距矩阵 P1 行 |
| 批次 5 | 运营与复盘 | 效果复盘/策略建议、开发信工作区（`OUTREACH-SEQUENCE-MIGRATION-DESIGN.md`）、结果反馈/触达资格、3D 雷达 | 各自迁移设计文档 |

SSE 实时事件不在任何批次承诺内：本批用有界轮询；SSE 需先评估 Framework/宿主能力，另行立项。

## 7. Delivery status

- [ ] W1 Run 监控（进度 + 台账 + 轮询）
- [ ] W2 候选分页 + 导出
- [ ] W3 批量审核/转换 + 短名单
- [ ] W4 Run 暂停/恢复（或按规则顺延并在此注明）
- [ ] W5 页面组件拆分
- [ ] 差距台账与本计划回写（§5）
