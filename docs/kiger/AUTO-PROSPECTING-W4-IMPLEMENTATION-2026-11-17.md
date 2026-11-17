# 自动获客 W4 — Run 暂停/恢复 批次 3 实现记录（2026-11-17）

> 依据 `AUTO-PROSPECTING-W4-PAUSE-DESIGN-2026-11-15.md` §2 已定稿语义与 §4 checklist。本文承接批次 3：先逐项定稿 §3 六个阻塞决策，再落地状态机。原"顺延判定"文档保持不变，作为历史证据。

## 1. 前置条件复核（相比 11-15 的变化）

| 项 | 11-15 状态 | 11-17 状态 | 依据 |
|---|---|---|---|
| 预览整栈 | 未监听 | 9708 / 9800 / 5433 在线，W3 四端点真实 HTTP 200、PG 并发幂等取证 | `.workbuddy/memory/2026-11-16.md` |
| C 级证据基础 | 无 | 已具备（可实机启动 + HTTP 冒烟） | 同上 |
| W4 顺延理由 | 并发语义未定稿 + 无 C 级基础 | 本轮逐项定稿（§2），C 级基础已具备 → 解除顺延 | 本文 |

## 2. §3 六个阻塞决策定稿

统一原则：**把 `Paused` 当作"暂停运行态"而非第 3 个终态**，最大化复用现有租约/fencing 不变量，只对"写入 fencing"和"心跳续约"两处谓词做最小放宽；所有终态结算谓词天然排除 `Paused`，不改。

| # | 冲突点 | 定稿决策 | 证据等级 |
|---|---|---|---|
| 1 | finalize 误写终态 | Worker 在每个 `CancelRequested` 检查点旁并列检查 `Paused`。`break` 出 provider 循环后、进入 finalize 前统一判 status：`Paused` → 走**暂停专用出口**（停心跳 + `ReleaseLeaseForPauseAsync` 清租约/fencing+1，`CompletedAt` 保持 NULL，不写任何终态）后 `return`。心跳与 in-flight 写入在 `Paused` 下继续（放宽谓词见 #6），保证已进入的 provider 调用可落盘。 | A（源码）+ B（新增测试） |
| 2 | 认领 SQL 源态集合 | `TryClaimLeaseAsync` 只认领 `Queued` / 过期 `Running`，`Paused` 天然不在谓词中 → 不被认领，无需改 SQL。`Resume` 写回 `Queued`（清租约/fencing+1）后复用 `TryClaimLeaseAsync`，**不新增** Paused→Running 认领分支，不复制 fencing 仲裁。 | A + B |
| 3 | 异步父 Run 路径 | `Resume` 目标态按**是否存在 extraction jobs** 推导（对 §2 第三态补全）：无任何 job → `Queued`（同步 Run，入 `runQueue`）；存在 job（含全部已 terminal）→ `WaitingForExtractions`，复用 `GetWaitingForExtractionsIdsAsync` + coordinator 结算或等待。暂停期间子任务凭独立租约继续跑完、落盘不受影响；父 Run 为 `Paused` 时 coordinator 因 `Status != WaitingForExtractions` 直接 return，不会误结算。 | A + B（含 PG 门控） |
| 4 | 过期恢复与启动重排 | `RecoverExpiredLeasesAsync`（status IN Running/CancelRequested/Waiting）、`RecoverInterruptedAsync`（Running/CancelRequested 且无租约）、`GetQueuedIdsAsync`（Queued）三处谓词**已天然排除 `Paused`**，不改 SQL；补契约守卫测试固化，防止后续误加 Paused 复活分支。 | A + B |
| 5 | 取消路径源态 | `CancelRunAsync` 增 `Paused` 分支：因暂停出口已清租约、无 Worker 持证人，直接写 `Cancelled` + `CompletedAt` + fencing+1（与 `WaitingForExtractions` 分支同构），不写 `CancelRequested`（无人结算会悬空）。`CancelFenced*` 保持只接受 `CancelRequested`，不改。取消与恢复并发：两者均为带状态前置条件的条件 UPDATE，先到者胜；`Paused` 只被其中一方消费，天然互斥，取消不静默失败。 | A + B（并发用例） |
| 6 | 仓储结算谓词 | 终态结算 `CompleteFenced*` / `FailFenced*` / `CompleteAsync` / `FailAsync` 谓词均为 Running/Waiting/CancelRequested，天然排除 `Paused` → 暂停期间阻断，不改。仅放宽两个**非终态**谓词：`TryLockLeaseForWriteAsync` 与 `RenewLeaseWithDatabaseClockAsync`/`RenewLeaseAsync` 的 status 增列 `Paused`（同一 lease_token + fencing 才放行，不削弱归属仲裁）。 | A + B |

### fencing 不变量自检

- 放宽的只有"持证人写入/续约"谓词，仍需 `tenant_id + basic_id + lease_token + fencing_version` 全部匹配 + 租约未过期；`Paused` 只由 `TryPauseAsync` 从有证状态写入，租户外/伪造 token 仍被拒。
- `Paused` 期间唯一能写终态的是 `CancelRunAsync`（用户显式取消，直接 Cancelled）与 `ResumeRunAsync`（回非终态），二者均带 `status=Paused` 前置条件。
- 暂停出口 `ReleaseLeaseForPauseAsync` 带 lease_token + fencing 守卫，只有持证人能清；清后 `Paused` 无租约，`RecoverExpiredLeases` 也不碰它 → 稳定等待用户 Resume/Cancel。

## 3. 落地 checklist 状态（§4 对应）

- [x] 1. §3 六项定稿（本文 §2）
- [x] 2. 枚举 `Paused = 8`（int 列，无 CHECK 约束，无 DDL）
- [x] 3. 仓储 `TryPauseAsync` / `TryResumeAsync` / `ReleaseLeaseForPauseAsync` / `HasExtractionJobsAsync` + 两处谓词放宽（写入 fencing、心跳续约）
- [x] 4. Worker 暂停专用出口（`RunPausedException` + 5 个检查点 + catch 释放租约）+ 心跳放行 Paused + Resume 复用现有队列/coordinator
- [x] 5. Application `PauseRunAsync` / `ResumeRunAsync`（复用 `crm:execute`，无新权限码）+ Cancel 放行 Paused（领域服务）
- [x] 6. 契约接口 / 前端 API facade + types + 7 语言 i18n + 状态显隐（暂停/恢复/取消按钮）+ 轮询集合排除 Paused
- [x] 7. 契约守卫测试 12/12 + PG 仓储状态机集成测试 5/5 + **Worker 端到端集成 1/1（真实 PG 仓储 + fake provider + 反射驱动 ProcessItemAsync）** + 全量 Api.Tests 1129/0失败 + AI.Tests 582/582 + 前端 type-check 通过
- [x] 8. 实机 HTTP：Pause/Resume 路由存在、200 状态转换（7→8→0）、终态 422、未登录 401；越权账号与浏览器逐键交互未取证

### 7 项细粒度行为测试的诚实标注

批次 2 计划 §Acceptance gates 列出的 Worker 行为用例，本轮取证程度不一，不夸大：

| 用例 | 取证方式 | 结果 |
|---|---|---|
| 暂停保留租约、不写终态/CompletedAt | PG 集成测试（真实 UPDATE RETURNING） | ✅ 已取证 |
| 暂停出口清租约 + fencing+1、幂等拒绝 | PG 集成测试 | ✅ 已取证 |
| Resume 推导 Queued/Waiting、清租约、拒绝非 Paused | PG 集成测试 | ✅ 已取证 |
| 租户隔离（他租户不能 Pause/Resume） | PG 集成测试 | ✅ 已取证 |
| Worker 5 个检查点观察 Paused + 专用出口不结算 | 源码契约守卫 | ✅ 已取证（静态） |
| 暂停后不再派发 / in-flight 落盘 / 断点续跑（端到端 Worker 行为） | **Worker 端到端集成测试（真实 PG + fake provider）** | ✅ 已取证 |
| 暂停中取消 / 暂停中异步子任务语义（端到端） | 领域取消分支源码守卫 + coordinator 父态守卫 | ⚠️ 仅静态 |

### 端到端测试发现并修复的真实缺陷（2026-11-17 续）

补 `CrmProspectingPauseResumeWorkerIntegrationTests` 时，反射驱动真实 `ProcessItemAsync` + 真实 PG 仓储 + fake provider（在 provider 调用内部触发 Pause），暴露了三个静态守卫与仓储单测都没抓到的集成缺陷：

| # | 缺陷 | 现象 | 修复 |
|---|---|---|---|
| 1 | Provider execution / coverage 的 fenced 写入谓词只认 `Running` | Pause 落在 provider 调用内部后，checkpoint 落盘 `TrySaveCheckpointAsync` 失败 → 走通用 catch → 状态停在 Paused 但租约不清、卡死 | `CrmProspectingRunProviderExecutionRepository` 5 处 + `CrmProspectingProviderRequestRepository` coverage 1 处谓词改为 `status IN (Running, Paused)`，与已放宽的 `TryLockLeaseForWriteAsync` 一致 |
| 2 | stale-attempt 恢复把 Paused 当"非活跃" | Paused 持有保留租约，但 `NOT IN (Running, CancelRequested)` 会把其 in-flight attempt 误判为 stale 并恢复 | 恢复谓词加入 Paused：`NOT IN (Running, Paused, CancelRequested)` |
| 3 | `TryTransitionAsync` 的 null `int?` 参数被 Npgsql 推断为 text | Resume 重放 checkpoint 的 `Checkpointed→Projecting` 转换不传 count，`COALESCE(@projectedRecordCount, integer列)` 报 42804 | SQL 改为 `COALESCE(CAST(@x AS INTEGER), 列)`；此缺陷为既有问题，仅 Resume 重放路径首次触发 |

三者均补了源码契约守卫防回退（`InFlightFencedWrites_ShouldAdmitPausedAsAnActiveRunState`、`ProviderStepTransition_ShouldCastNullableIntParametersForPostgres`）。端到端测试断言：Pause 后 alpha 调用 1 次、beta 0 次、状态 Paused、租约清空、fencing+1、alpha step=Checkpointed 且 beta step=Queued；Resume 后 alpha 不重复调用、beta 调用 1 次、Run 进入终态——全部通过。

## 4. 证据等级

| 等级 | 内容 | 结果 |
|---|---|---|
| A 代码事实 | 六处谓词、3 套租约路径、finalize 结算、coordinator 父态守卫均本轮直接读源码核对 | 已取证 |
| B 自动化测试 | 契约 10/10；PG 仓储状态机 5/5；全量 Api.Tests 1121/0失败；AI.Tests 582/582；type-check 通过 | 已取证（端到端 Worker 行为用例未覆盖） |
| C 运行验收 | 9708/9800/5433 恢复；Pause 7→8、Resume 8→0 真实 HTTP 200；终态 422；未登录 401 | 已取证；越权账号 + 浏览器逐键未取证 |
| D 关闭判定 | 按 §5 五条规则：1-4 满足，第 5 条「Windows 原生部署验收」仍缺，且越权/浏览器未取证 → **不关闭**，标记「后端实机已验收；Windows 原生部署与 UI 交互待验」 | 未关闭 |

### PG 门控全量测试的既有失败（非 W4 回归）

带 `KIGER_TEST_POSTGRES_CONNECTION_STRING` 跑全量 Api.Tests：1126 总 / 10 失败，均为既有 `CrmProspectingBulkConvertPostgresIntegrationTests`（candidate→lead 竞态）、`CrmProspectingExtractionPostgresIntegrationTests`（extraction job 租约）、`CrmConversionClaimPostgresIntegrationTests`（lead/candidate claim）——这些领域本轮**一行未改**，失败点为种子/库状态依赖（`Assert.Single` 空集合），与 Run 暂停/恢复状态机无关。单独复跑其中一个仍失败，确认非并发干扰。W4 新增 5 个 PG 测试全部通过，未列入失败。不带连接串的基线全量 1121/0失败 是本轮回归判定依据。
