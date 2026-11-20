# Haituo CRM → KIGER CRM 功能对齐开发计划（2026-11-17）

> 依据 `HAITUO-CRM-MIGRATION-GAP-REGISTER.md`（P0/P1/P2 缺口台账）、`HAITUO-CRM-FUNCTION-MIGRATION-MATRIX.md`（4 节 25 行功能矩阵）、`HAITUO-CRM-FUNCTION-PARITY-AUDIT-2026-11-06.md`（24 行等价审计）与 `AUTO-PROSPECTING-BATCH-2-PLAN-2026-11-11.md` §6 批次路线。
> 本文是**执行计划**，不是验收结论；"已实现"不等于"已对齐"，关闭仍以台账「关闭规则」5 条为准。

## 1. 口径与执行规则

1. **关闭规则 5 条**（缺一不可）：源端功能/API/权限/数据结构已定位；目标端实现与 DB 结构已定位；正常/异常/权限/并发/恢复路径已测试；自动化测试 + 必要的 PostgreSQL/外部服务集成通过；**Windows 原生部署实机验收完成**。
2. **一次一个垂直切片**：Domain → DTO/Dynamic API → 仓储/迁移 → 前端类型化 API/页面 → 测试 → 门禁 → 浏览器实机。禁止半套协议、双轨入口、静默 fallback。
3. **同一契约同一提交**：后端 DTO/服务名/权限码/菜单 Component/前端 `*.types.ts`/facade/调用点/测试/i18n/文档同批变更；Conventional Commits 中文；不推送、不执行升级脚本（只新增脚本文件，由启动自动执行）。
4. **门禁基线**：后端 `dotnet build backend/XiHan.BasicApp.slnx -c Release --no-restore` + `dotnet test --solution ... -c Release --no-build`，对照基线 `Api.Tests` 1129/0 失败、`Ai.Tests` 582/582（PG 门控 10 个既有失败单列）；前端 `pnpm type-check|lint|test|build` 对照基线（既有 `pnpm check` 699 problems、1 个 forks worker 随机超时单列）。不删断言、不降门禁、不扩大 allowlist。
5. **范围决策先于编码**：源端语义未定稿的项（市场分析是否属产品范围、Agent skills/knowledge、SSE）先写决策记录，不擅自造第二套 CRM/渠道插件。
6. **实测口径**：静态搜索、设计文档、代码存在、一次成功请求都不能单独关闭条目；每片必须声明证据等级 A（真实 HTTP/PG）、B（自动化）、C（运行）、D（关闭判定）。

## 2. 现状盘点（三档）

| 档位 | 数量 | 内容 | 欠账性质 |
|---|---|---|---|
| **A 已实现待验收** | 约 13 项 | CRM 主业务 6 域（客户/联系人/公海/线索/商机/跟进/单据）+ 自动获客核心 6 域（活动策略/Provider/身份/候选评分/转化/Worker）+ 异步 Provider 与 PG 租约并发（W1–W5 已落地） | **仅差验收证据**：Windows 原生部署、浏览器逐键、越权账号、11 个 Provider 真实 Run；代码与自动化测试已闭环（后端 1129/0） |
| **B 部分实现** | 约 6 项 | 超级搜索规划、深度挖掘、官网发现、身份解析（GLEIF 仅 exact-name）、覆盖记忆持久化、外发审批逐入口核对 | 策略/骨架在，工作流、持久化、API/UI 或逐入口审计缺 |
| **C 完全缺失** | 15 个功能域 | 见 §3 里程碑 M1–M5 | 需从领域模型开始新建；2026-11-17 已用 `backend/src` 实测确认无匹配实体/服务 |

> 本轮代码核对事实：无 `SysCrmProspectingSchedule`/`OutreachSequence`/`MarketAnalysisRun`；`SuperSearchPlanner`/`DeepMiningPolicy`/`WebsiteDiscoveryPolicy`/`CoverageMemoryPolicy`/`CrmGleifIdentityResolver` 均为**纯策略类、无生产调用方**；WhatsApp Meta webhook 验签/解析/Inbox 已落地，但 `Baileys` 零匹配；无 `ExamService`/`BusinessCardOcr`/`ShipmentOcr`/`CustomsExport`。

## 3. 里程碑与功能映射

### M0 — 验收欠账收口（与 M1 并行，非代码工作）

- 重启预览整栈（PG 5433 + WebHost 9708 + Vite 9800；当前全部离线）。
- 浏览器逐键：W3 多选/批量条键盘可达、W4 暂停/恢复按钮、亮暗/窄视口/reduced motion。
- 越权账号取证；11 个公开 Provider 真实 Run/许可；升级脚本空库/既有库/平台库实测；Windows 原生部署（关闭规则第 5 条）。
- 单列既有债：PG 门控 10 个失败（3 个 PG 集成测试文件的种子/库状态依赖）、前端 699 problems。
- **出口**：差距台账 A 档条目升级证据等级；第 25/26 行改判仅在规则 5 取证后进行。

### M1 — 批次 3 剩余：搜索能力与定期计划

| 切片 | 源端入口 | 目标现状 | 交付物 | 验收门 |
|---|---|---|---|---|
| **M1.1 超级搜索规划预览**（首选开工） | `prospect-super-search.ts` | `CrmProspectingSuperSearchPlanner` 纯策略类无调用方 | DTO + `[DynamicApi]` QueryService（`PreviewSuperSearch`，`Execute` 权限）+ 前端类型化 API 与规划预览面板 + 7 语言；**只读、不写库、不改 Run** | 输入校验/深度上限/排除项传播/指纹确定性测试；权限与租户/匿名拒绝；浏览器预览交互 |
| M1.2 Mission 持久化 + Run 接线 | 同上 | 规划结果不落地 | mission 实体/迁移/仓储 + 保存/列表/获取 API + 从规划生成 Run 的显式命令（复用现有 StartRun，不复制派发逻辑） | 幂等、租户隔离、状态回写、真正创建 Run 的端到端测试 |
| M1.3 自然语言目标解析 | 计划 §6 | 无 | 目标解析为规划输入的受控映射（不引入自由文本直接执行） | 歧义拒绝、注入/越权防护、可解释输出 |
| M1.4 定期计划 Schedule | `prospect-schedules.ts`、`prospect-scheduler.ts` | 无 `SysCrmProspectingSchedule` | 时区安全 daily/weekly/monthly 递归 + 审批 + 幂等 Run 创建 + Scheduler Worker；复用现有 Run 队列与租约/fencing | 递归边界（月末/闰年/DST）、重复触发幂等、暂停/取消、租户隔离、PG 集成 |

### M2 — 批次 4：资格四步与终态报告

| 切片 | 源端入口 | 目标现状 | 交付物 |
|---|---|---|---|
| M2.1 GLEIF discover/reconcile 扩展 | `prospect-identity-bootstrap.ts` | 仅 exact-name resolver | 权威等级、分阶段 bootstrap、证据历史、人工复核流程 |
| M2.2 联系人补全与验证报告 | `prospect-verification.ts` | 未证明存在 | 字段级验证策略/报告；未验证字段不得自动成为权威 CRM 值 |
| M2.3 官网 probe 工作流 | `prospect-website-discovery.ts` | 策略/传输骨架 | 有界页面抓取、重定向复核、证据持久化、审核 UI |
| M2.4 终态/无结果报告 | 差距矩阵 P1 | 无 | Run 终态与无结果原因的结构化报告与导出 |

### M3 — 批次 5：运营、外联与复盘

| 切片 | 源端入口 | 目标现状 | 交付物 |
|---|---|---|---|
| M3.1 效果复盘/策略建议 | 计划 §6 | 无 | 基于台账的复盘指标与建议（复用现有报表查询，不造第二套 CRM） |
| M3.2 开发信/外联序列 | `outreach-sequences.ts`、`40-development-email.json` | 无 `OutreachSequence` | 序列定义、enrollment、步骤调度、抑制/退订、**人工审批后发送**、发送结果回写 |
| M3.3 触点/回复/跟进 | `prospect-outreach.ts`、`email-tracking.ts` | 无 | 触点与事件模型、回复分类、跟进任务、签名追踪事件去重 |
| M3.4 结果反馈/触达资格 | `prospect-outcome-feedback.ts`、`prospect-outreach-eligibility.ts` | 无 | 反馈闭环与资格判定，反馈不得静默覆盖 CRM 权威字段 |
| M3.5 3D 雷达 | 计划 §6 | 无 | 范围与依赖确认后实现（明确数据源与权限） |

### M4 — 沟通渠道（P0/P1，需产品范围决策）

| 切片 | 源端入口 | 目标现状 | 交付物 |
|---|---|---|---|
| M4.1 WhatsApp Meta 收口 | `whatsapp-plugin/src/server/providers/meta-provider.ts` | webhook 验签/解析/事件键/Inbox 迁移已有 | 账号路由、Inbox 消费/Outbox 发送、实时消息、加密凭据存储、发送审批；Meta 真实联调需外部许可 |
| M4.2 WhatsApp Baileys | `baileys-provider.ts`、`provider-manager.ts` | 零匹配 | QR/会话认证、断线重连、设备注销、账号健康；协议风险评估先出决策记录 |
| M4.3 历史聊天导入 | `services/whatsapp-history-import.ts` | 无 | 预览、字段映射、去重、失败重试、导入审计 |
| M4.4 企业微信 | `wecom-command-gateway.ts`、`wecom-connector-driver.ts` | 无 | OAuth/回调签名、用户绑定、命令授权、消息收发、审计 |

### M5 — 工具与运营完整性（P1/P2，全部未排期）

- **M5.1 OCR**：名片 `business-card-ocr.ts`、出货/贸易单据 `shipment-ocr.ts`、`trade-document-import.ts`；复用平台文件存储 + OCR Provider + 置信度/人工校正/确认后写入 Lead/Shipment。
- **M5.2 考试系统**：`exam-system-test.ts`；独立有界领域（题库/试卷/限时/判分/补考/成绩权限）。
- **M5.3 知识资料库**：`knowledge-assets-test.ts`；AI `KnowledgeDocument` 仅候选，需补审核发布/版本/附件/标签/ACL/引用/复训触发。
- **M5.4 报表看板**：销售漏斗、国家分布、跟进健康度、团队排行；复用现有查询与权限，不造第二套 CRM。
- **M5.5 外贸小工具**：海关数据导出 `customs-export.ts`；先范围决策（是否属本次范围）。
- **M5.6 P2 体验项**：历史导入、多语言/翻译原文译文分离、媒体管理（授权/过期/限制/病毒策略）、自动化节奏（默认关闭+审批+频率+暂停+审计）、数据迁移工具（幂等/断点/错误报告/回滚）。

### 待范围决策（不进入排期，先出决策记录）

- Agent skills/knowledge（`agent-skills/*`、`agent-knowledge/*`）：源端运行时资产是否属 KIGER CRM 产品需求。
- SSE 实时事件：批次 2 计划明示不承诺，需先评估 Framework/宿主能力，另行立项。
- 市场分析/贸易观察/市场机会事实（`market-analysis-runs.ts`、`market-trade-observations.ts`、`market-opportunity-facts.ts`）：独立有界领域 vs 显式延期。

## 4. 依赖与风险

1. **源端语义**：`hongshuoguo2-cloud/haituo-crm` 需逐项核对；未在源端定位的语义不得凭文档臆造，标"需源端核对"并先出设计。
2. **运行环境**：预览整栈当前离线，M0 与浏览器验收需先重建重启；Windows 原生部署需用户环境配合。
3. **外部服务**：Meta/Baileys/WeCom/OCR/邮件真实联调涉及凭据与许可，测试使用桩 + 显式 opt-in，不外发真实消息。
4. **既有债不滚入新片**：PG 门控 10 失败与前端 699 problems 单列；新片必须零新增回归。
5. **范围膨胀**：M4/M5 共 11 项尚未排期，按"每片先查目标现有能力 → 复用/适配/迁移"执行，禁止第二套 CRM 或硬塞渠道逻辑进 CRM Domain。

## 5. 当前执行切片：M1.1 超级搜索规划预览

- [x] 后端 DTO：`CrmProspectingSuperSearchPreviewInputDto`（列表 + depth + requestedRounds）与 `CrmProspectingSuperSearchPreviewDto`（scopeFingerprint/maximumRounds/plannedRounds/providerCount/totalQueryCells/rounds → queryCells）。
- [x] Mapper：domain record → DTO 显式映射（禁止反射/双字段）。
- [x] QueryService：`[DynamicApi]` 方法 `PreviewSuperSearchAsync`，`[PermissionAuthorize(CrmPermissionCodes.Execute)]` + `[HttpPost]`，无 DB 写入、无 Run 创建；异常语义沿用 planner（参数校验 `ArgumentException`/`ArgumentOutOfRangeException`）。
- [x] 后端测试：输入校验、深度上限、排除项逐轮传播、指纹确定性、DTO 映射、权限元数据、契约守卫（源码字符串断言）。
- [x] 前端：`prospecting.types.ts` 镜像 DTO；`prospecting.ts` facade 路由 `PreviewSuperSearch`；预览面板组件（深度/轮次/范围输入 + 轮次表格 + 指纹），XiHan.UI 语义令牌、键盘可达。
- [x] 7 语言 i18n 对称新增键（validate 未出现 superSearch 缺失/孤儿键）。
- [x] 门禁：后端 build 0 错误、解决方案测试 5993 总 / 0 失败（Api.Tests 1133/0）；前端 type-check 0、改动文件 lint 0（全量 699 既有债持平）、test 181 文件 / 2639 用例全绿、build 成功；px/token 门禁未新增条目。
- [ ] 浏览器亮暗/窄视口/键盘验收：**待 M0 预览整栈恢复**（9708/9800/5433 当前离线，无法取证，不标记完成）。
- [ ] 单提交（同一契约同批）；**不推送**。

### M1.1 证据记录（2026-11-17）

- A 代码事实：`CrmProspectingSuperSearchQueryService`（只读、无仓储、Execute 权限）+ `ICrmProspectingSuperSearchQueryService` + 4 个 DTO + 显式 Mapper + 前端 facade `'PreviewSuperSearch'` 与 `SuperSearchPreviewPanel.vue`（`role="status"`、`<details>` 可键盘展开、错误清空旧结果）。
- B 自动化测试：后端新增 `CrmProspectingSuperSearchPreviewTests` 4/4；全量 Api.Tests 1133/0；前端 API 契约 + 组件契约测试全量通过；type-check/lint/test/build 全部对齐基线。
- C 运行验收：未取证（预览整栈离线）；真实 HTTP 权限 401/403、租户、异常路径待 M0 恢复后补。
- D 关闭判定：本片是"API/UI 接线"，不关闭任何迁移条目；等价审计第 7 行由"策略迁移、工作流部分"推进为"只读规划 API/UI 可用，Run 执行仍未接线"。

> 完成 M1.1 后按同一切片模板继续 M1.2 → M1.3 → M1.4 → M2…；每片完成即回写本文件与差距台账，未取运行时证据前不关闭迁移条目。

### M1.2 证据记录（2026-11-18）

- [x] 实体 `SysCrmProspectingSuperSearchMission` + 状态枚举 `Planned/Executed` + 升级脚本 `UpdateScripts/5.9.43/5.9.43.sql`（含 RequestKey 软删唯一索引）；仓储接口/实现 + DI 注册。
- [x] 3 个 Dynamic API：`SaveSuperSearchMission`（Create）、`GetSuperSearchMissions`（Read）、`CreateRunFromSuperSearchMission`（Execute），复用既有权限码；规划在服务端重算，客户端快照不被信任。
- [x] Run 接线：`CreateRunFromSuperSearchMission` 委托既有 `StartRunAsync`（无第二套派发），执行前校验 mission 的 provider 集合被已批准激活的策略覆盖；重放返回同一 Run，不重复入队。
- [x] 前端：`*MissionCreateDto`/`*MissionDto` 类型、3 个 facade、面板扩展（活动选择/名称/请求键/保存/列表/创建 Run），保存后刷新列表、创建 Run 后 `runCreated` 事件让页面刷新 Run 区；7 语言再补 10 键。
- [x] 测试：新增 `CrmProspectingSuperSearchMissionSqliteIntegrationTests` 5/5（幂等、冲突、越权/跨租户、Run 复用重放、策略覆盖拒绝、契约守卫）。
- [x] 门禁：后端解决方案 build 0 错误、测试 **5998 总 / 0 失败**（Api.Tests 1138/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、test 181 文件 / 2639 用例全绿、build 成功；px/token/i18n 无新增条目。
- [ ] 浏览器实机与真实 HTTP 401/403、租户隔离、真实 PG Run 创建：**待 M0 预览整栈恢复**，不标记迁移条目完成。
- [ ] 单提交（同一契约同批）；**不推送**。

> **诚实边界**：mission 的规划快照已持久化并驱动 provider 集合校验，但现有 Run 执行单元仍是"每 provider 一次"的既有管线；多轮 query cell 的逐轮执行、暂停/恢复联动属于 M1.3+ 与 W4 后续，不在本片宣称完成。
>
> 下一步：M1.3 自然语言目标解析 → M1.4 定期计划 Schedule → M2 资格四步。

### M1.3 证据记录（2026-11-18）

- [x] 领域解析器 `CrmProspectingObjectiveParser`：只接受固定双语词表的 `标记: 值` 行；未知/重复标记、自由文本行、行内嵌套标记、非法深度/轮次、控制字符与超长输入一律拒绝；市场中文名/ISO 代码归一，未知市场保留并给警告；provider 代码按白名单字符校验。
- [x] 只读 Dynamic API `ParseSuperSearchObjective`（复用 `Execute` 权限）+ DTO/显式 Mapper：输出"规划输入 + 逐字段匹配证据 + 警告"，可解释、可复核；**只映射到表单，不自动保存、不创建 Run、不调用 LLM/Provider**。
- [x] 前端：类型/facade + 面板"目标描述 → 解析为规划"交互（解析结果填表、展示匹配与提示、清空旧预览）；7 语言再补 5 键。
- [x] 测试：`CrmProspectingSuperSearchPreviewTests` 新增 3 例（双语字段映射/歧义与注入式输入拒绝/确定性与前后端契约）。
- [x] 门禁：后端 build 0 错误、解决方案测试 **6001 总 / 0 失败**（Api.Tests 1141/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、test 181 文件 / 2639 用例全绿、build 成功；px/token/i18n 无新增条目。
- [ ] 浏览器实机与真实 HTTP/越权取证：**待 M0 预览整栈恢复**。
- [ ] 单提交（同一契约同批）；**不推送**。

> **诚实边界**：解析是受控的标记语法映射，不是通用自然语言理解；未识别的标记必须报错而非猜测。规划结果仍需人工复核后才可保存为 mission。
>
> 下一步：M1.4 定期计划 Schedule → M2 资格四步。

### M1.4 证据记录（2026-11-18）

- [x] 实体 `SysCrmProspectingSchedule` + 频率/状态枚举（含 Rejected）+ 升级脚本 `UpdateScripts/5.9.44/5.9.44.sql`（幂等键与 due 查询索引）；仓储 `GetDueAsync` 与 CAS `TryClaimAsync`。
- [x] 时区递归 `CrmProspectingScheduleRecurrence`：本地墙钟计算 + IANA/Windows 时区互转；DST 空洞前进到首个有效分钟、回折取较早 UTC 瞬时；月末 31 号截断与 -1 取月末（闰年 2/29 正确）。
- [x] 审批生命周期 API（Create/Read/Execute 复用既有权限码）：创建 → 提交 → 批准（计算 NextRunAtUtc）→ 暂停/恢复/取消；调度派发**不作为 API 暴露**，仅 Worker 可用。
- [x] Worker `CrmProspectingScheduleHostedService` + `CrmProspectingScheduleDispatcher`：确定性 occurrence 请求键复用 `StartRunAsync`，CAS 保证多实例不重复派发；派发失败保留 due 状态供重试。
- [x] 前端 SchedulePanel（活动选择、递归表单、审批/暂停/恢复/取消按状态显隐）+ 类型/facade/7 语言 30 键；页面在计划变更后刷新 Run 区。
- [x] 测试：递归 6 例（interval/weekday/月末/闰年/DST/结束日期）+ SQLite 4 例（生命周期、幂等与越权、派发与 CAS、契约守卫）。
- [x] 门禁：后端 build 0 错误、解决方案测试 **6011 总 / 0 失败**（Api.Tests 1151/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、test 181 文件 / 2639 用例全绿、build 成功；px/token/i18n 无新增条目。
- [ ] 浏览器实机、真实 HTTP、PG 门控与 Windows 原生部署（关闭规则第 5 条）：**待 M0**，不关闭迁移条目。
- [ ] 单提交（同一契约同批）；**不推送**。

> **诚实边界**：SQLite 夹具对 `DateTimeOffset` 回读会重挂本地偏移，测试改为断言壁钟与 CAS 而非跨夹具比较瞬时；生产 PG 保留真实瞬时。多实例并发仅在 CAS/请求键层面取证，未做真实双 Worker PG 压测。
>
> 批次 3 至此收口；下一步进入 **M2 资格四步**（GLEIF discover/reconcile、联系人补全、验证报告、官网 probe）。

### M2 证据记录（2026-11-18）

- [x] 领域策略 `CrmProspectingVerificationPolicy`：四步收口为可解释报告——权威身份分阶段引导（仅精确 LEI 匹配算 Verified）、官网候选复用既有 review-only 策略、联系人/国家证据佐证、终态/无结果报告（verdict + blockers + missing + actions）。
- [x] 只读 API `CrmProspectingQualificationAppService`：`ResolveIdentity`（显式调用权威解析器）、`AssessWebsite`（纯策略、不抓取）、`BuildVerificationReport`、`GetVerificationReport`；复用 Execute/Read 权限码；报告落库为 append-only 实体 + 迁移 `5.9.45`。
- [x] **不自动改写权威字段**：报告是冻结快照；生成报告不调用 GLEIF/不抓取网页（身份匹配由显式命令提供），不批准候选、不创建 Lead。
- [x] 前端：`VerificationPanel.vue` 嵌入候选审核详情（解析身份 / 评估官网 / 生成报告 + 字段级结果与建议），类型/facade/7 语言 21 键；权限门控。
- [x] 测试：策略 3 例（缺口报告、精确身份+证据、不安全官网/身份冲突阻断）+ SQLite 2 例（报告冻结与候选不变、越权/不存在拒绝）+ 契约守卫。
- [x] 门禁：后端 build 0 错误、解决方案测试 **6016 总 / 0 失败**（Api.Tests 1156/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、test 181 文件 / 2639 用例全绿、build 成功；px/token/i18n 无新增条目。
- [ ] 浏览器实机、真实 HTTP、GLEIF 真实联调与 PG/Windows 验收：**待 M0**，迁移条目不关闭。
- [ ] 单提交（同一契约同批）；**不推送**。

> **诚实边界**：GLEIF 仍是"精确匹配"层，分阶段 discover/reconcile 与证据历史尚未扩展；证据实体只存哈希与来源、不存字段值，因此"联系人补全"是**佐证级**而非值抽取；官网 probe 只做策略评估，未做有界页面抓取与重定向复核。这三项列入 M2 后续深化，不在本片宣称完成。
>
> 下一步：M2 深化（分阶段身份/页面抓取）或 M3 效果复盘与开发信工作区，视源端语义取证结果排序。

### M2 深化证据记录（2026-11-18）

- [x] 分阶段身份：`CrmGleifIdentityResolver.DiscoverAsync` 暴露有界候选集（结果集不完整时 fail closed 返回空集，不提供部分视图），`ResolveExactAsync` 仍只在唯一精确匹配时返回；`CrmProspectingIdentityBootstrapPolicy` 按"归一化名称 → 国家兼容 → 唯一提升"输出阶段/候选/步骤，歧义一律 `NeedsOperatorReconciliation`。
- [x] 有界官网抓取：`CrmProspectingPinnedPageFetcher` 复用 DNS pinning 处理器，HTTPS-only、256KB 上限、10s 超时、跨主机重定向拒绝、最终 URL 二次策略校验；成功抓取才写入 `SysCrmProspectingEvidence`（`website_probe`、unverified、URL+body 哈希幂等），失败返回带 reason 的失败结果，不伪造成功。
- [x] API：`BootstrapIdentity`、`ProbeWebsite`（Execute）追加到资格服务；前端面板新增两按钮与阶段/候选/步骤/抓取结果展示，7 语言再补 9 键。
- [x] 测试：分阶段策略 1 例 + 服务接线 1 例 + 抓取证据幂等/失败不落库 1 例 + unsafe URL 零网络 1 例；既有 GLEIF 契约守卫按真实实现加强（唯一性 + 歧义拒绝双重校验）。
- [x] 门禁：后端 build 0 错误、解决方案测试 **6020 总 / 0 失败**（Api.Tests 1160/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、test 181 文件 / 2639 用例全绿、build 成功；px/token/i18n 无新增条目。
- [ ] 浏览器实机、GLEIF/Meta 真实联调、真实抓取与 PG/Windows 验收：**待 M0**，迁移条目不关闭。
- [ ] 单提交（同一契约同批）；**不推送**。

> **诚实边界**：页面抓取只取哈希/标题/字节数供人工复核，不解析正文为 CRM 事实；分阶段身份未持久化"决策历史"实体，步骤随请求返回但不入库（报告快照可再引用）。SSE/实时事件仍不在范围。
>
> 下一步：M3（效果复盘、开发信工作区、结果反馈/触达资格）或转入 M0 运行时验收。

### M0 运行时验收证据（2026-11-18）

- **整栈**：PG 5433 + WebHost 9708（重编 Debug 含全部新代码）+ Vite 9800 全部监听；日志确认 `CrmProspectingScheduleHostedService` 已启动。
- **真实 HTTP（A 级）**：登录 200；新端点未登录 401；`PreviewSuperSearch` / `ParseSuperSearchObjective` / `SaveSuperSearchMission` / `SuperSearchMissions` / `CreateSchedule` / `Schedules` / `BuildVerificationReport` / `GetVerificationReport` / `ProbeWebsite` 均 200；Schedule 提交→批准→暂停→恢复→取消全 200，终态 Cancelled；已批准策略内 provider 的 mission 创建 Run 200，**重放返回同一 RunId**；策略覆盖不足时 422（守卫生效）；opt-in 解析器未开启时 Bootstrap 返回 Unresolved + `resolver_not_registered`、Resolve 明确失败。
- **实机暴露并修复的真实缺陷**：① `ICrmProspectingIdentityResolver` 是 opt-in 注册，新服务强依赖导致启动期 DI 校验失败 → 改为可选依赖 + 诚实降级；② Schedule API 属独立 DynamicApi 服务且 `CreateScheduleAsync` 路由去掉 `Create` 前缀，前端 facade 误用旧服务名/路由 → 修正；③ 前端 Schedule DTO 用 `startDate` 而后端是 `startDateUtc`，导致 400 → 对齐；④ `CreateRunFromSuperSearchMissionAsync` 路由为 `RunFromSuperSearchMission` → 修正；⑤ `BootstrapIdentity`/`ResolveIdentity` 为 POST，前端误用 GET → 修正。
- **浏览器（C 级）**：获客页新面板全部渲染；候选详情中的资格验证面板可生成/展示报告；点击"分阶段解析身份"真实 POST 成功并显示阶段结果；暗/亮主题切换、390px 窄视口（按钮换行、禁用态正确）均正常；无控制台错误。截图：`.openchamber/screenshots/m0-prospecting-{dark,light,mobile}-*.jpg`。
- **门禁**：后端解决方案 build 0 错误 / 测试 **6021 总 / 0 失败**；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **181 文件 / 2649 用例全绿**、build 成功。
- **仍未取证**：Windows 原生部署（关闭规则第 5 条）、PG 门控并发、opt-in GLEIF 真实联调、11 个 Provider 真实 Run。迁移条目按规则继续不关闭。
>
> 下一步：M3 或在用户要求下推进 Windows 原生部署验收。

### M3 证据记录（2026-11-18）

- [x] 实体/迁移：外联序列、入组、触点三表 + `5.9.46`（入组与序列唯一索引含 IsDeleted）；触点只存内容哈希、审批人与时间。
- [x] 领域策略 `CrmOutreachPolicy`：步骤计划校验（1–20 步、渠道/延迟/模板边界）、资格判定（已认证候选/未抑制/邮箱要求）、回复分类、下一步动作（拒绝→抑制、兴趣→1 天跟进、休假→7 天同步骤、完成）。
- [x] API `CrmOutreach`（Create/Read/Execute 复用既有码）：序列创建/激活/暂停、入组（幂等）、触点列表/审批/记录结果、抑制、效果复盘；**系统不自动发信**——未审批触点记录返回 422，只有审批后才可记录 Sent/Replied。
- [x] 前端 OutreachPanel + 类型/facade + 7 语言 31 键；窄视口按钮换行正常。
- [x] 测试：策略 1 例 + SQLite 3 例（人工审批闭环/抑制与资格/契约守卫）。
- [x] 门禁：后端 build 0 错误 / **6025 总 / 0 失败**（Api.Tests 1165/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **181 文件 / 2651 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP（A 级）：创建/激活序列 200；匿名 401；入组 200；未审批记录 422；审批 200；记录已发送 200；复盘计数（sent=1/planned=1）正确；抑制 200（status=3 + reason）。浏览器面板渲染并在 UI 创建序列成功。
- [ ] Windows 原生部署、真实邮件/WhatsApp 发送（本片明确不自动化）、PG 门控：**待取证**，迁移条目不关闭。
>
> **诚实边界**：本片只完成"人工执行 + 审计 + 效果复盘"闭环；模板变量替换、真实渠道发送、退订链接与频率限制属于后续渠道集成，未在本文宣称完成。M3.5 3D 雷达仍需先做范围决策。
>
> 下一步：M3 余项（3D 雷达范围决策）或 Windows 原生部署验收。

### M4 证据记录（2026-11-18/19）

- [x] 实体/迁移：租户自有 WhatsApp 账号绑定 `5.9.47`（WABA/phone 唯一索引含 IsDeleted），只存密钥库引用、不存 Token/AppSecret；复用既有持久收件箱的唯一键。
- [x] 幂等摄取 `WhatsAppMetaWebhookIngestService`：按 WABA+phone 路由租户（无绑定则 fail-closed），事件键用既有工厂（含 migration 5.9.40 之前的 legacy key 兼容检查），数据库唯一索引兜底；Webhook POST 仅在持久写入后返回 200，否则 503 让 Meta 重试。
- [x] 管理 API `WhatsAppChannel`（binding 增删改隔离、绑定列表、inbox 复核，Read/Manage 权限码）+ 前端渠道面板（嵌入聊天审计页、按钮码 `message.chat-audit.whatsapp_manage` 遵循权限卫生）。
- [x] 测试：摄取路由/持久化/重放幂等/legacy key/契约 5 例；既有 Webhook 契约守卫按新实现加强（仍校验原始体签名、413、UTF-8、401）。
- [x] 门禁：后端 build 0 错误 / **6030 总 / 0 失败**（Api.Tests 1170/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **181 文件 / 2651 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP：匿名 401；绑定 API（`/WhatsAppChannel/UpsertBinding`、`/Bindings`、`/Inbox`）200；Webhook 无 token GET 401、未签名 POST 401；浏览器面板读到 HTTP 创建的绑定。
- [ ] Meta 真实签名事件联调、Baileys/企业微信、真实发送与 Windows 原生部署：**待取证**。
>
> **诚实边界**：本片只收口 Meta Webhook 的"验签→路由→持久收件箱→确认"链路；Baileys（QR/会话/重连/设备注销）、企业微信连接器、外发 Outbox 与真实渠道发送均未完成；消息正文仅存哈希/原始 envelope 供复核，不自动投影为 CRM 事实。
>
> 下一步：M4 余项（Baileys/企业微信需范围与协议取证）或 Windows 原生部署验收。

### M4 历史聊天导入证据（2026-11-19）

- [x] 领域/实体：`ChatHistoryImportPolicy` 纯校验（500 行上限、字段/时间校验、文件内与跨批次 externalId 去重、内容只存 SHA-256）；批次/行实体 + 迁移 `5.9.48`（行唯一索引含 IsDeleted）。
- [x] API `ChatHistoryImport`：`Preview` 纯校验零写入；`Import` 生成可审计批次与逐行判定；`Batches`/`Rows` 复核；Read/Manage 权限码。无效/重复行也作为审计记录落库，不会伪造成功。
- [x] 前端面板嵌入聊天审计页（JSON 行粘贴、预览/暂存导入、逐行判定与批次列表）+ 类型/facade + 7 语言 15 键；按钮码沿用 `message.chat-audit.whatsapp_manage`。
- [x] 测试：策略 1 例 + SQLite 2 例（零写入预览、导入与重放去重）+ 契约守卫（不投影会话）。
- [x] 门禁：后端 build 0 错误 / **6033 总 / 0 失败**（Api.Tests 1173/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **181 文件 / 2651 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP：匿名 401；Preview 200（imported=2/invalid=1，零落库）；Import 200；重放 200（imported=0/duplicates=2）；Batches/Rows 200；浏览器面板渲染。
- [ ] 真实历史文件（CSV/XLSX）解析、消息投影进会话、失败重试队列与 Windows 原生部署：**待取证**。
>
> **诚实边界**：导入目前只生成"暂存审计行"，不自动创建会话或消息；解析输入限于 JSON 行数组（CSV/XLSX 与字段映射属后续）。Baileys/企业微信仍未实现。
>
> 下一步：M4 余项（Baileys/企业微信）或由用户指定 Windows 原生部署验收。

### M4 企业微信命令网关证据（2026-11-19）

- [x] 领域/实体：租户自有 WeCom 绑定（只存密钥库引用）与命令审计实体 + 迁移 `5.9.49`；SHA-1 排序验签与命令白名单（help/status/ping）纯策略，未知命令只审计不回复、不触发外部动作。
- [x] 回调端点：GET 验证 / POST 命令回调；未签名 401，**加密回调 fail-closed 503**（未配置解密密钥），已签名回调按 CorpID+AgentID 路由租户并审计 MsgId 幂等。
- [x] 管理 API `WecomGateway`（绑定维护/列表/命令审计，Read/Manage）+ 前端面板嵌入聊天审计页 + 7 语言 11 键。
- [x] 测试：验签确定性/篡改拒绝、命令白名单、网关契约（加密 fail-closed、无外发、迁移）3 例。
- [x] 门禁：后端 build 0 错误 / **6036 总 / 0 失败**（Api.Tests 1176/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **181 文件 / 2651 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP：匿名 401；未签名回调 401；加密回调 503；绑定 API（实机修正路由为 `/WecomGateway/UpsertBinding`）与列表/审计 200；浏览器面板渲染。
- [ ] 真实企业微信加解密配置、OAuth 用户绑定、命令回发与 Windows 原生部署：**待取证**。
>
> **诚实边界**：本片仅接受明文 preview 回调；真实企业微信消息为加密 XML，未配置解密密钥时一律 503 fail-closed，不伪造接收成功。不对接真实发送消息。
>
> 下一步：M4 余项（Baileys QR/会话/重连、WeCom 解密与回发）或用户指定 Windows 原生部署验收。

### M4 Baileys 会话契约与范围决策（2026-11-19）

- **范围决策**：本片**不接入真实 Baileys 协议客户端**。理由：QR 会话、设备身份、断线重连与设备注销涉及真实账号/设备安全风险，且需要用户明确授权外部会话后才会实现；当前只交付"可审计的会话契约 + 操作者显式确认的状态机"，避免出现半套协议或伪造连接成功。原 `HAITUO-CRM-FUNCTION-MIGRATION-MATRIX.md` 中 WhatsApp Baileys 行保持"需源端逐项核对"，不因本片改判完成。
- [x] 实体/迁移 `5.9.50`：租户自有 Baileys 会话，只存加密认证状态**引用**，不存 QR 载荷、密钥或设备身份；AuthStateRef 在设备注销时清空。
- [x] 领域策略 `BaileysSessionPolicy`：QR 5 分钟时效、显式确认连接、重连退避上限 30 秒、健康度分级（未知/健康/降级/离线）、设备注销（Revoked）为终态不可恢复。
- [x] API `BaileysSession`（Create/Read/Manage）与前端会话面板（发起扫码/确认连接/注销），7 语言 14 键；按钮码沿用后端登记码。
- [x] 测试：状态机 2 例 + 契约守卫（应用层不出现协议客户端/QR 载荷）1 例。
- [x] 门禁：后端 build 0 错误 / **6039 总 / 0 失败**（Api.Tests 1179/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **181 文件 / 2651 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP：匿名 401；创建/发起扫码/确认连接/注销均 200；注销后 AuthStateRef 清空；恢复已注销会话 422（终态生效）；会话列表 200。
- [ ] 真实 Baileys 客户端接入、QR 图片、会话加解密、断线自动重连、设备注销外部调用、Windows 原生部署：**明确待用户授权与取证**。
>
> 下一步：批次 4 的功能并行线（如报表/看板、知识资料库、外贸工具）或 Windows 原生部署验收；M4 余下项按范围决策暂缓。

### 批次 4 报表看板证据（2026-11-19）

- [x] 只读 QueryService `CrmDashboardQuery`：总览（线索/商机/金额/候选/Run/活跃 Run）、线索漏斗、候选来源、商机阶段；线索与商机遵循 `IUserDataScopeFilterService`，候选/Run 遵循租户隔离；无写入、无外部调用。
- [x] 前端看板面板嵌入获客页：指标卡 + 三组 SVG 条形（无新图表依赖），XiHan.UI 语义令牌，7 语言 11 键。
- [x] 测试：数据范围与租户过滤、分组与占比、只读契约 3 例；前端 API 路由（实机修正为 `CrmDashboardQuery` 服务名）与组件契约。
- [x] 门禁：后端 build 0 错误 / **6042 总 / 0 失败**（Api.Tests 1182/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **181 文件 / 2653 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP：匿名 401；四个报表端点 200（线索 4 / 候选 50 / 来源 ror 25 + seed 25）；浏览器面板渲染真实数据。
- [ ] 时间范围筛选、团队排行、跟进健康度、看板权限码细分与 Windows 原生部署：**待取证**。
>
> 下一步：批次 4 余项（知识资料库、外贸工具/考试/OCR 等 P1）或 Windows 原生部署验收。

### 批次 4 知识资料审核证据（2026-11-19）

- [x] 在既有平台知识文档之上新增独立审核实体 `5.9.51`（不改动索引状态字段）：文档哈希、审批人、意见、审批时间，平台数据隔离。
- [x] 策略 `KnowledgeReviewPolicy`：仅首次提交或原文变更需重审；驳回必须填写理由；每份文档保留完整决策历史。
- [x] API `KnowledgeReview`（Submit/Decide/Reviews，Execute/Read）与前端审核面板嵌入 AI 知识页；7 语言 12 键。
- [x] 测试：纯策略 1 例 + SQLite 审核闭环（提交→驳回理由校验→通过→未变更拒绝重提→修改后重提）1 例 + 契约守卫 1 例。
- [x] 门禁：后端 build 0 错误 / **6052 总 / 0 失败**（Api.Tests 1185/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **182 文件 / 2654 用例全绿**、build 成功；i18n/px/token 无新增条目；同步修复 `AiApplicationSurfaceStructureTests` 服务数量守卫（10→11）与 `enum-contract.test.ts` 的 modules api glob 误引入测试文件。
- [ ] 实机审核流：预览账号无 `ai:read`/`ai:execute` 权限（已知 403）；实机仅确认新路由已注册（403 而非 404），完整审批链路由集成测试覆盖。
- [ ] 知识库 ACL/引用来源、增量索引与 Windows 原生部署：**待取证**。
>
> 下一步：批次 4 余项（考试、OCR、外贸工具等 P1）或 Windows 原生部署验收。

### 批次 4 销售培训考试证据（2026-11-20）

- [x] 归属：CRM 子域（不新建模块），租户隔离实体 + 迁移 `5.9.52`：题库、试卷、考试尝试；答案与解析只在服务端。
- [x] 规则 `CrmExamPolicy`：题量 1–50、时长 1–180 分钟、及格分在总分内、最多 10 次、重考间隔、超时判定、按题积分评分；未发布试卷不可开考，同一尝试不可重复交卷。
- [x] API `CrmExam`（Question/Questions/Paper/Papers/PublishPaper/Start/Submit/MyAttempts，Create/Read/Update/Execute）与前端考试面板嵌入获客页；候选端 DTO 与 wire 类型均不含 `CorrectAnswer`/`Explanation`；7 语言 27 键。
- [x] 测试：纯策略 1 例 + SQLite 闭环 2 例（发布/评分/判卷/重复交卷拒绝、超时与重考上限）+ 契约守卫 1 例；前端 API 路由与候选端 wire 守卫 2 例。
- [x] 门禁：后端 build 0 错误 / **6056 总 / 0 失败**（Api.Tests 1189/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **183 文件 / 2656 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP：匿名 401；建题/列表/建卷/发布/开考/交卷/尝试列表 200；未发布开考 422；交卷得分 60/100 且状态=通过；实机修正服务名为 `CrmExam`（类名去 AppService，非 `Exam`）；浏览器面板渲染正常。
- [ ] 多选题、题库分类/抽题策略、考试证书与 Windows 原生部署：**待取证**。
>
> 下一步：批次 4 余项（名片/单据 OCR、外贸工具等 P1）或 Windows 原生部署验收。

### 批次 4 名片 OCR 录入证据（2026-11-20）

- **范围边界**：不接入真实 OCR 引擎；由操作者提供已抽取字段（JSON 数组），系统只做确定性校验、归一化与查重，确认才创建 Lead。迁移 `5.9.53`。
- [x] 实体/策略：租户隔离的待审核名片行（含归一化键、重复原因、驳回原因、Lead 引用）；`CrmBusinessCardPolicy` 解析 ≤200 行、姓名/公司必填其一、联系方式必填其一、邮箱/电话格式、手机号去符号、官网主机归一化、状态机（仅 Pending 可确认/驳回且驳回必填理由）。
- [x] API `CrmBusinessCardImport`（Preview/Import/Confirm/Reject/Rows/Batches，Read/Create/Execute）：预览零写入；导入暂存全部有效行（重复行保留审计但标记，非法行明确计数不落库）；确认经既有 `CrmLeadDomainService` 创建 Lead。Lead 仓储新增手机号查重。
- [x] 前端面板嵌入获客页（JSON 录入、预览校验/重复、批次与逐行确认/驳回）+ 类型/facade + 7 语言 17 键。
- [x] 测试：策略 1 例 + SQLite 闭环 2 例（预览零写入→导入→确认生成 Lead→重复阻断→驳回理由校验） + 契约守卫 1 例；前端 API 路由与无外部客户端守卫 2 例。
- [x] 门禁：后端 build 0 错误 / **6060 总 / 0 失败**（Api.Tests 1193/0）；前端 type-check 0、改动文件 lint 0（全量 699 持平）、`vitest run` **184 文件 / 2658 用例全绿**、build 成功；px/token/i18n 无新增条目。
- [x] 实机 HTTP：匿名 401；预览 200（3 行→2 有效/1 重复，批次为空证明零写入）；导入 200（暂存 2、非法 1、重复 1）；行列表/批次 200；确认有效行 200 且返回 Lead ID；重复确认 422；无理由驳回 422、有理由驳回 200；浏览器面板渲染正常。
- [ ] 真实 OCR 引擎/图片解析、字段映射模板、CSV/XLSX 批量、Windows 原生部署：**待取证/待授权**。
>
> 下一步：批次 4 余项（外贸单据 OCR 与导入、海关数据导出/外贸小工具等 P1）或 Windows 原生部署验收。
