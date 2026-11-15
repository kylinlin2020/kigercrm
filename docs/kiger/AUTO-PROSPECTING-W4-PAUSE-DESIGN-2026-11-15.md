# 自动获客 W4 — Run 暂停/恢复 语义定稿与顺延判定（2026-11-15）

> 本文只做**语义定稿与风险取证**，不改状态机、不引入半套协议。依据 `AUTO-PROSPECTING-BATCH-2-PLAN-2026-11-11.md` §3 W4「先设计定稿，再动状态机」与「顺延规则」。

## 1. 结论（A 级：代码事实）

**W4 整体顺延批次 3**，本批次不落地 `Paused` 状态机。理由不是工作量，而是 6 个尚未定稿的并发语义决策（§3），其中 3 个会改变既有租约/fencing 不变量；在「预览后端/前端/PG 均未实机验收」的前提下引入第 3 个非终态，无法给出 C 级证据。按计划顺延规则，W4 不阻塞 W1–W3/W5 的验收与登记。

不做的具体范围：不追加 `CrmProspectingRunStatus.Paused`、不加 `PauseRun`/`ResumeRun` Dynamic API、不加权限种子、不加前端按钮。避免「后端半套协议 + 前端静默兼容」。

## 2. 语义定稿（批次 3 实现时以此为准，不再重新讨论）

| 项 | 定稿语义 |
|---|---|
| `PauseRun` 允许源态 | `Queued` / `Running` / `WaitingForExtractions` → `Paused`。其余状态（含终态、`CancelRequested`、`Cancelled`）明确拒绝并返回业务冲突，不静默成功。 |
| 暂停的作用域 | 只停止**新** provider 派发与**新**异步子任务提交。已进入的 provider 调用允许完成，并按现有台账路径落盘；已提交的异步子任务凭独立租约继续，不受父 Run 暂停影响。 |
| 暂停不是终态 | 暂停期间禁止写入 `Succeeded` / `PartiallySucceeded` / `Failed` / `Cancelled` 之外的任何终态结算，且**不得**因 Worker 循环 break 触发 finalize 结算。`CompletedAt` 保持 NULL。 |
| `ResumeRun` | `Paused` → 依台账推导回 `Queued`（无已提交子任务、无进行中 provider 步）或 `WaitingForExtractions`（存在未完成子任务）；恢复必须重新入队，唤醒复用现有 `CrmProspectingRunMessage` 队列与启动恢复机制，不新增第二套触发器。 |
| 取消优先 | 暂停中收到取消 → 走既有 `CancelRequested` / `Cancelled` 路径；`CancelFenced*` 必须允许 `Paused` 作为源态。取消与恢复并发时取消胜出。 |
| 租约过期恢复 | `RecoverExpiredLeasesAsync` / `TryClaimLeaseAsync` / `GetQueuedIdsAsync` 均不得把 `Paused` 复活为 `Running` 或重新入队。 |
| 前端 | 按钮按状态显隐；`Paused` 纳入 W1 轮询集合时定稿为「不自动轮询、保留手动刷新」，避免低频轮询掩盖恢复入队事实。 |

## 3. 阻塞性决策点（B/C 级证据：现状实现）

| # | 冲突点 | 现状代码事实 | 必须先定稿的决策 |
|---|---|---|---|
| 1 | finalize 会误写终态 | `CrmProspectingRunHostedService.cs:151-157` 每步循环只检查 `CancelRequested` 后 `break`；`875-990` 在循环结束后统一走 `CompleteFenced*` / `CancelFenced*` 结算 | 暂停退出必须新增独立的「保留断点、不结算」出口；心跳与租约在暂停期间是继续保活还是释放（影响 in-flight 写是否能过 `TryLockLeaseForWriteAsync`） |
| 2 | 认领 SQL 的源态集合 | `CrmProspectingRunRepository.cs:64-100` `TryClaimLeaseAsync` 只认领 `Queued` 或「`Running` 且租约过期」，并把状态强制写为 `Running` | `Paused` 必须显式排除；`Resume` 是把状态写回 `Queued` 复用认领，还是新增专门的 Paused→Running 认领分支（后者要复制 fencing 仲裁） |
| 3 | 异步父 Run 路径 | `CrmProspectingRunRepository.cs:626-697` `TrySetWaitingForExtractionsAsync` / `GetWaitingForExtractionsIdsAsync` 以 `WaitingForExtractions` + 子任务终态集合做聚合；`700+` 另有 `TryClaimWaitingForExtractionsLeaseAsync` | 父 Run 暂停时子任务继续跑完，父 Run 恢复后的聚合查询必须能看到这些结果；若暂停期间子任务全部完成，谁来触发父 Run 结算、以何种状态结算 |
| 4 | 过期恢复与启动重排 | `CrmProspectingRunHostedService.cs:1395-1419` 启动时 `RecoverExpiredLeasesAsync` → `RecoverInterruptedAsync` → `GetQueuedIdsAsync` 重排；`CrmProspectingRunRepository.cs:784+` / `830+` 为对应实现 | 三处均需 Paused 排除谓词；恢复重排是否允许把 Paused 当作「可恢复中断」处理（当前语义会把中断标 `NeedsReview`） |
| 5 | 取消路径源态 | `CrmProspectingRunRepository.cs:391-432` `CancelFencedWithDatabaseClockAsync`、`545-570` `CancelFencedAsync` 仅接受 `CancelRequested` | 增加 `Paused` 源态后，取消优先级测试需覆盖「暂停 + 取消」「暂停 + 恢复」并发 |
| 6 | 仓储层结算谓词 | `CrmProspectingRunRepository.cs:880-964` `CompleteLegacySyncAsync` / `FailAsync` 以 `Running` + 空租约元数据为条件，并有 `Cancelled` 兜底 | 暂停期间这些路径必须被阻断，否则 in-flight 落盘会与暂停状态互相覆盖 |

## 4. 批次 3 落地 checklist（依赖顺序）

1. 本文 §3 六项逐项决策定稿 → 写入 W4 验收记录，指定 A/B/C/D 证据等级。
2. 追加枚举值 `Paused = 8`；核对全库无 status CHECK 约束（现有 `EnsureSchemaConstraintsAsync` 只处理 provider request 约束，`CodeFirst` 不生成 CHECK）。
3. 仓储：新增 `TryPauseAsync` / `TryResumeAsync` 条件 UPDATE（带 `tenant_id`、状态源集合、租约/fencing 守卫）；同步修改 §3 表中全部 SQL 谓词；升级脚本（枚举为 int 列，预期无 DDL，但需脚本登记）。
4. Worker：暂停专用退出路径、心跳策略、恢复唤醒入队；异步子任务 Worker 不改。
5. Application：`PauseRunAsync` / `ResumeRunAsync` + `[PermissionAuthorize(CrmPermissionCodes.Execute)]`；Cancel 路径放行 Paused；权限种子幂等。
6. 契约：`ICrmProspectingAppService`、DTO、前端 `*.types.ts` / API facade / 7 语言 / 菜单权限可见性一次收口。
7. 测试：暂停后不再派发、in-flight 落盘、恢复断点续跑、暂停中取消、暂停中异步子任务语义、过期租约不复活 Paused；既有租约/fencing 测试零回归；PG 门控集成测试。
8. 恢复预览后做真实 HTTP 冒烟（未登录/越权/正常路径）与浏览器实机，再按 GAP 关闭规则 5 条判定。

## 5. 证据等级

| 等级 | 内容 | 结果 |
|---|---|---|
| A 代码事实 | 枚举无 `Paused`；三套租约路径与 finalize 结算行为如上，均为本轮直接读取源码核对 | 已取证 |
| B 自动化测试 | 本轮未改状态机，故不产生新增测试；既有租约/fencing 测试保持基线 | 不适用（顺延） |
| C 运行验收 | 预览 9708 / 9800 / 5433 未监听；W3 的 4 个批量端点真实 HTTP 冒烟亦未完成 | **未完成** |
| D 关闭判定 | W4 计划条目按顺延规则标注「顺延批次 3」，**不关闭**；差距台账不改判 | 顺延 |
