# 自动获客功能完整迁移差异审计

**审计日期：** 2026-11-04  
**源项目：** `hongshuoguo2-cloud/haituo-crm`（main）  
**目标项目：** `kylinlin2020/kigercrm`（kiger-dev）  
**目标 PR：** #1（Draft；不得合并）  
**审计性质：** 基于两仓库当前可读取源码、文件树和迁移设计文档的静态盘点；不等同于运行验收。

## 1. 结论

**当前不能判定为完整迁移。** 目标仓库已经具备 Campaign / Strategy / Run、后台 Worker、Candidate、Evidence、Score、人工审核及转现有 Lead 的第一阶段业务闭环；但源项目中的多项独立能力在目标仓库没有发现对应实现，且目标端已新增 USAspending 官方采购适配器，但单一来源覆盖不足，仍不能视为完整真实获客能力。

迁移目标是按 KIGER 的 .NET / SqlSugar / PostgreSQL / SaaS tenant 架构重建业务能力，不是把 TypeScript 源代码直接复制到目标后端。

## 2. 功能差异矩阵

状态定义：
- **已有（待运行验收）**：目标源码可见对应实现，但未因此推断构建或运行成功。
- **部分实现**：目标有基础版本，缺少源项目的关键语义或运营能力。
- **未发现实现**：本次目标分支文件树与已检查代码未发现等价模块；后续需继续检查动态注册、外部服务等可能入口。
- **不迁移/复用**：复用 KIGER 已有 CRM 能力，不复制第二套主数据。

| 源项目能力 | 源项目代表实现 | 目标项目现状 | 判定 / 后续 |
|---|---|---|---|
| Campaign 管理 | `prospect-campaigns.ts` | Campaign 实体、API、前端工作区 | 已有（待运行验收） |
| 策略版本、审批、激活 | `prospect-strategies.ts` | Strategy 版本与审批流程 | 已有（待运行验收）；需补差异规则核对 |
| Run 幂等、状态与取消 | `prospect-runs.ts` | Run API、持久化、取消 | 部分实现；分页、筛选、完整进度/终态报告待补 |
| 普通搜客 / 搜索计划 | `lead-finder-launch.ts`、`prospect-search-planner.ts` | SearchObjective + ProviderPlan 基础字段 | 部分实现；源端参数合并、搜索计划预览和解释尚未确认 |
| 深度挖掘 / Super Search | `prospect-deep-mining.ts`、`prospect-super-search.ts` | 未发现等价服务/API/UI | 未发现实现 |
| Provider 目录与能力元数据 | `provider-catalog.ts`、`provider-contract.ts` | 仅 `ICrmProspectingProvider` 与 Fake Provider | 部分实现；目录、能力/许可/字段权威级别缺失 |
| 实际外部搜索来源 | `lead-providers.ts`、`ai-search-provider.ts` | 目标已新增 opt-in 的 USAspending 官方采购 Provider；其他真实来源尚未接入 | 未完成；无真实生产来源 |
| 官方企业身份来源 | GLEIF、Companies House、KVK、SEC 等 provider | 未发现真实适配器 | 未完成 |
| 采购 / 招标来源 | `public-procurement-providers.ts`、EU TED、World Bank、USASpending、UK Contracts Finder | 未发现真实适配器 | 未完成 |
| 国际贸易来源 | `trade-providers.ts`、UN Comtrade、US Census、Eurostat | 未发现真实适配器 | 未完成 |
| 商业联系人补全 | `prospect-contact-enrichment.ts` | 未发现等价服务 | 未发现实现；商业 API 凭据/配额/成本控制也缺失 |
| AI 网站发现 | `prospect-ai-website-discovery.ts` | 未发现等价服务 | 未发现实现 |
| 网站发现与验证 | `prospect-website-discovery.ts` | 未发现等价服务 | 未发现实现 |
| 企业身份解析 / 权威档案 | `prospect-identity-bootstrap.ts`、`prospect-identity-authority-profiles.ts` | 未发现独立身份档案与解析服务 | 未发现实现 |
| 覆盖记忆 / 已处理企业去重 | `prospect-coverage-memory.ts` | 当前有 Run 内 Candidate 唯一键及 Lead 邮箱/域名重复筛查 | 部分实现；缺少跨 Run、来源和时间维度的覆盖记忆 |
| 原始来源请求账本 / 重放 | `prospect-provider-request-ledger.ts`、`prospect-source-raw.ts` | Run.RawResultsJson + Candidate/Evidence | 部分实现；缺少独立请求账本、原始来源版本与重放语义 |
| 候选规范化 / 去重 / 流水线 | `prospect-candidate-pipeline.ts` | Candidate 规范化、邮件/域名筛查、Evidence/Score | 部分实现；尚未对齐源端完整 pipeline 状态与合并规则 |
| 候选操作 / shortlist | `prospect-candidate-actions.ts`、`prospect-shortlist-http-test.ts` | 审核、转 Lead；未发现 shortlist API | 部分实现 |
| 资格评估与工作流 | `prospect-qualification.ts`、`prospect-qualification-workflow.ts` | Scorecard + 人工审核门槛 | 部分实现；源端资格工作流、版本和门槛需逐项对齐 |
| 候选验证 | `prospect-verification.ts` | Evidence verification 字段与 review detail | 部分实现；缺少完整独立验证工作流 |
| Lead 转化 | `prospect-lead-conversion.ts`、`prospect-customer-conversion.ts` | 转换已批准 Candidate 到现有 CRM Lead | 已有基本入口；Customer 转化复用现有 CRM 流程，不另建 Customer |
| 定时计划 / Scheduler | `prospect-scheduler.ts`、`prospect-schedules.ts` | 未发现 Schedule 实体/API/UI | 未发现实现 |
| Queue 协调与 worker kernel | `prospect-queue-coordinator.ts`、`prospect-execution-kernel.ts` | Redis queue + HostedService | 部分实现；租约/心跳/写入 fencing 尚未接入 |
| 运行事件 / 实时进度 | `prospect-live-events.ts` | Run 汇总状态及列表刷新 | 部分实现；实时事件、阶段耗时、来源级进度未确认 |
| 来源请求日志 / 配额 / 成本 | `provider-request-logging.ts`、`provider-response-cache.ts` | 未发现统一运行时 | 未发现实现 |
| Provider 网络安全 | `provider-network-security.ts`、`provider-http-client.ts` | 尚未发现统一 HTTP 执行边界 | 未完成；必须先做 SSRF、DNS/IP、redirect、超时和响应大小保护 |
| 结果反馈 / 策略优化 | `prospect-outcome-feedback.ts` | 未发现等价服务 | 未发现实现 |
| 触达资格判断 | `prospect-outreach-eligibility.ts` | 不在当前目标 UI/API 中 | 未发现实现；若迁移只做资格判断，不自动发送邮件 |
| Agent 搜客 Skill / 工具语义 | `agent-skills/prospecting/SKILL.md`、`30-prospecting.json` | 未发现等价 Agent skill/tool registry | 未发现实现；需按目标 Agent 框架确认接入点 |
| 人工审核及审计 | 源端 candidate actions / pipeline | Candidate review、Evidence、Score detail | 已有基本能力；需核验 reviewer、决策理由和审计事件完整性 |
| CRM 主数据 / 客户转化 | 源端 conversion flow | 目标已有 CRM Lead / Customer | 不迁移第二套 CRM 主数据，复用现有功能 |

## 3. 已从目标源码确认的实现范围

- Campaign、Strategy、Run、Candidate、Evidence、Score 实体及对应 Repository。
- Campaign/Strategy/Run 应用服务、API DTO 和前端 `prospecting/index.vue` 工作区。
- 策略审批/激活、Run 创建/取消/历史读取。
- Redis 延迟队列和 HostedService；当前仅 Fake Provider 有可见注册实现。
- Candidate 基础规范化、邮箱/网站 host 重复筛查、Evidence 及初始 Score 快照。
- Candidate 人工审核、审核详情和转换到现有 CRM Lead。
- 租户隔离和若干幂等/并发保护基础。

以上仅为源码存在性确认，不代表全部 API 在真实环境可用，也不代表 PostgreSQL migration、Redis worker、前端构建或 Windows 验收通过。

## 4. 关键阻塞

1. **真实来源覆盖不足**：已增加 USAspending 官方联邦采购 Provider，但当前仍只有单一美国采购来源，缺少身份注册、国际招标、贸易数据和通用发现来源。
2. **来源执行安全缺失**：未发现统一受控 HTTP 边界、SSRF/DNS/redirect 防护、限流、配额、成本账本和缓存。
3. **候选闭环不完整**：跨 Run 覆盖记忆、身份解析、网站/联系人补全、验证和 shortlist 等能力未对齐。
4. **调度与可观测性不足**：Schedule、来源级阶段进度、实时事件和结果反馈尚未对齐。
5. **Worker 安全未完成**：原子租约、独立心跳、子事务 fencing、终态 fencing 和过期恢复尚未接入 HostedService。
6. **运行验收缺失**：无可见 GitHub Actions 运行记录；当前工作环境没有 .NET SDK；Windows 原生编译、真实 PostgreSQL 双连接和端到端运行均未完成。

## 5. 完成迁移的实施顺序

### P0 — 真实获客来源与安全执行基础
- 扩展 Provider 契约：分页、游标、字段级证据、来源记录 ID、错误类别、重试、配额、成本、取消。
- 实现统一安全 HTTP 客户端：HTTPS host allowlist、DNS/IP 目标检查、redirect 逐跳验证、响应字节上限、超时、限流、重试预算。
- 建立租户级 Provider 配置与加密凭据；前端可查看来源启用状态，但不回显密钥。
- 第一批优先官方身份、公开采购和贸易数据；随后搜索引擎；商业 enrichment 默认关闭且需客户自行配置密钥。

### P1 — Provider 真实适配器
- 第一批：GLEIF / Companies House（官方身份）、EU TED / World Bank / UK Contracts Finder（公开采购）、UN Comtrade / US Census（贸易）。
- 第二批：Serper / Brave / SerpAPI（发现来源）。
- 第三批：Apollo / People Data Labs / Hunter（付费联系人补全，显式配额与成本确认）。
- 每个 Adapter 需 contract tests、条款/覆盖范围说明、错误/配额行为和真实 source URL。

### P2 — 企业身份与候选管线
- 跨 Run coverage memory、identity authority profile/bootstrap、冲突证据处理。
- 网站发现、联系人补全、验证、深度挖掘。
- 对齐候选 pipeline 状态、shortlist、qualification workflow、人工审核与 Lead 转化审计。

### P3 — 运营闭环
- Scheduler / Schedule、Run 阶段事件、来源级统计、成本/配额、缓存、请求账本与结果反馈。
- Agent skill/tool 接入，使 Agent 可创建任务、持续追踪终态、汇报真实数量和失败来源。
- 触达资格仅提供判断结果；所有外发邮件和网站发布仍必须人工审批。

### P4 — 并发与交付验收
- 完整接入 Worker lease/heartbeat/fencing/recovery。
- PostgreSQL 双连接竞争测试、迁移与 CodeFirst parity、租户隔离、失败回滚。
- Windows 原生 .NET restore/build/test、Redis、PostgreSQL、端到端来源采集到人工审核与 Lead 转化。
- 只有 P0–P4 的实现和验收证据齐全，才标记 100% 迁移。

## 6. 不变约束

- 不创建第二套 Lead / Customer / Contact。
- Provider 只能产出候选与证据，不能直接写入正式 CRM Lead。
- Candidate 必须人工审批后才能转 Lead。
- 不自动发送开发信、WhatsApp 或其他外联；所有外发行为必须人工批准。
- 不绕过登录、付费墙、CAPTCHA 或访问控制；遵守来源条款和隐私要求。
- 不将 AI 推断的邮箱、电话、公司身份或采购意向伪装成已验证事实。
- 保持 `main` 不动，继续只在 `kiger-dev` 开发，PR #1 保持 Draft，未经用户明确要求不合并。

## 7. 审计后的工作定义

迁移完成不是“页面能打开”或“Fake Provider 能跑”。最终验收必须证明：真实合法来源可执行、每条候选有可追溯证据、跨 Run 去重和身份冲突可审查、人工审批后复用现有 CRM Lead 转化、任务可恢复且有来源级进度/失败报告，并通过 Windows + PostgreSQL 真实运行测试。


## 8. 本轮实现增量（2026-11-04）

- 新增 `CrmUsaSpendingProspectingProvider`，调用固定官方 HTTPS API `api.usaspending.gov/api/v2/search/spending_by_award/`，无需 API Key。
- 适配器从产品范围、搜索目标和理想客户描述提取关键词，最多请求首 20 条联邦合同记录；映射企业名称、合同描述、机构、金额和合同日期，并保留官方 award URL、检索时间、payload SHA-256 与 `official_procurement` 证据分类。
- 响应限制为 6 MiB；仅接受固定 HTTPS host/path；DI fixed-endpoint HttpClient 禁止自动重定向；非成功 HTTP 状态不会把响应正文带入异常消息。
- 适配器默认不启用，只有配置 `XiHan:CRM:Prospecting:EnableUsaSpendingProvider=true` 才注册。Fake Provider 仍需独立显式 opt-in。
- 新增 provider 单元测试源和 wiring 回归断言，覆盖结果映射、空关键词不发请求、HTTP 429 错误处理、opt-in 注册和网络边界。

**验证状态：** 以上代码和测试已提交，但当前执行环境没有 .NET SDK；这些测试尚未编译或执行。USAspending 真实网络请求、数据条款审查和 Windows/PostgreSQL 端到端验收也尚未执行。适配器目前只读取第一页，若存在更多结果会明确产生 warning，不宣称分页已完成。


## 9. 第二个真实采购来源增量（2026-11-04）

- 新增 `CrmFindTenderProspectingProvider`，消费英国 Find a Tender 官方 OCDS release package API。
- 查询滚动 30 天更新窗口的 tender/award 阶段，最多读取 3 个 cursor 页、每页 100 条；本地按产品范围、搜索目标和 ICP 关键词筛选。
- 严格按 OCDS party `buyer` 角色提取采购方；不会将 supplier 角色误作采购客户。只有公开发布且格式有效的 buyer 官网、联系人和邮箱才会写入原始证据。
- 保留官方 Notice URL、发布时间、原始 release SHA-256 和 `official_procurement` 分类。
- 固定 HTTPS host/path、禁止重定向、30 秒超时、6 MiB 响应限制，并校验分页链接的目标 host/path。
- 显式启用配置：`XiHan:CRM:Prospecting:EnableFindTenderProvider=true`。
- 新增适配器测试，覆盖 buyer 映射、无关键词不发请求、supplier 不作为 buyer 的保护；新增 DI 网络边界回归断言。

**验证边界：** 代码和测试已提交，尚未在 .NET 10 环境编译/执行；也未进行实时 Find a Tender API 联调。适配器只读取 30 天窗口和最多 3 页，不应被描述为完整历史抓取。


## 11. Worker lease recovery increment (2026-11-04)

- Tenant startup recovery now calls the PostgreSQL-clock-based `RecoverExpiredLeasesAsync` before listing queued Runs for Redis reconstruction.
- Expired lease-backed `Running` Runs return to `Queued`; expired `CancelRequested` Runs become `Cancelled`. Lease-less legacy rows remain governed by the existing legacy recovery path.
- A wiring regression test asserts expired lease recovery occurs before queue reconstruction.

**Important boundary:** the HostedService still uses the legacy claim, child-write and terminal transition path. Atomic lease claiming, heartbeat/renewal, transactional child-write fencing and fenced terminal transitions remain unintegrated. Worker concurrency remains one; multi-instance operation is not approved. This increment is recovery groundwork, not completion of Worker fencing.


## 10. GLEIF identity enrichment increment (2026-11-04)

- Added `ICrmProspectingIdentityResolver` and `CrmGleifIdentityResolver` against the official GLEIF LEI Records API.
- The resolver is deliberately not registered as a lead-discovery provider. The worker invokes it only for already-created Candidates, with a maximum of 20 lookups per Run.
- Match policy is strict: normalized legal name must match exactly, and exactly one result must exist in the first 10 registry matches. Ambiguous/partial/fuzzy matches produce no identity.
- Stores immutable `registry` evidence linked to the candidate: LEI record ID, official GLEIF record URL, payload hash, entity status, LEI registration status, country and registered identifier. Evidence stays `unverified`; no score is automatically upgraded and no lead is created.
- Resolver is opt-in: `XiHan:CRM:Prospecting:EnableGleifIdentityResolver=true`. Network safety: fixed HTTPS host/path, redirects disabled, 20-second timeout and 2 MiB response cap.
- Added resolver tests and worker/DI wiring regression coverage.

**Verification boundary:** implementation and tests are committed but not compiled or run. The GLEIF live API has not been exercised from the target runtime.
