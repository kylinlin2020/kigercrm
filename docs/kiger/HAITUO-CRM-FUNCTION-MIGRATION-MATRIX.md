# Haituo CRM → KIGER CRM 功能迁移核对矩阵

> 审计基准：源仓库 `hongshuoguo2-cloud/haituo-crm` 的 `main` 树；目标仓库 `kylinlin2020/kigercrm` 的 `kiger-dev` 树。
>
> 本文是基于仓库目录、模块和入口文件的第一轮静态盘点，不等于逐接口、逐页面、逐业务规则验收。**“目标存在候选模块”不代表源功能已等价迁移**；只有完成行为、权限、数据、页面和测试对照后才能标记完成。

## 1. CRM 主业务

| 源项目功能域 | 源项目可核验入口 | 目标项目候选实现 | 初步结论 | 还需核对 |
|---|---|---|---|---|
| 客户档案/客户全景/联系人 | `customer-intelligence.ts`、`customer-maintenance.ts`、`10-customers.json` | CRM Customer/Contact AppService、QueryService、实体 | 已有核心实现 | 字段映射、健康度、分级、客户全景、时间线和权限行为 |
| 客户公海/客户分配 | `customer-public-pool.ts`、`11-customer-pool.json` | CRM 客户归属和数据范围相关实现 | 需逐项核验 | 公海规则、领取/退回、分配、回收、并发领取、操作审计 |
| 线索管理/线索转客户/删除回收 | `lead-deletion-test.ts`、`lead-finder-launch.ts`、`20-leads.json` | CRM Lead AppService/DomainService/Repository | 已有核心实现 | 回收站语义、删除恢复、线索来源、重复识别、转化后关联完整性 |
| 商机管道 | 源 README 的 Deal/Kanban 需求 | CRM Opportunity AppService/QueryService | 已有核心实现 | 阶段变更原因、赢率/预计成交日、拖拽行为、漏斗统计 |
| 跟进/活动/提醒/待办 | 源 README 的 Activity/Task 需求 | CRM Activity/Task AppService | 已有核心实现 | 逾期规则、报价后未回复/样品待确认提醒、日历视图、通知渠道 |
| 报价/订单/出货 | 源项目 README 中销售场景；目标已有 Quote/SalesOrder/Shipment | CRM Quote/SalesOrder/Shipment | 目标已实现更完整的履约链路 | PostgreSQL 并发、事务回滚、编号幂等、库存/数量边界和实机验收 |

## 2. 自动获客与线索开发

| 源项目功能域 | 源项目入口 | 目标项目候选实现 | 初步结论 | 还需核对 |
|---|---|---|---|---|
| 搜客计划/策略/活动/运行 | `prospect-search-planner.ts`、`prospect-strategies.ts`、`prospect-campaigns.ts`、`prospect-runs.ts` | CRM Prospecting Campaign/Strategy/Run | 已有对应核心模块 | 参数语义、计划预览、运行状态、暂停/恢复和终态 |
| Provider 执行/请求账本/覆盖记忆 | `prospect-provider-dispatcher.ts`、`prospect-provider-request-ledger.ts`、`prospect-coverage-memory.ts` | CRM Provider Registry/Request/Coverage/Execution | 已有对应实现 | 每个 Provider 行为、限流、幂等、覆盖记忆一致性 |
| 企业发现/身份解析/去重/验证 | `prospect-identity-bootstrap.ts`、`prospect-verification.ts`、`prospect-website-discovery.ts` | CRM IdentityResolver、CandidateFactory、各 Provider | 已有对应实现 | 身份权威等级、误合并防护、来源证据、联系人补全 |
| 候选清洗/评分/资格审核 | `prospect-candidate-pipeline.ts`、`prospect-qualification-workflow.ts`、`prospect-scorecard.ts` | CRM Candidate/Evidence/Score 和审核状态 | 已有对应实现 | 评分权重、证据可追溯、人工可联系门槛、拒绝/复审流程 |
| Candidate → Lead → Customer | `prospect-lead-conversion.ts`、`prospect-customer-conversion.ts` | CRM ProspectingAppService、Lead/Customer 转化 | 已有对应实现 | 完整应用层事务、并发胜者/失败方、无孤儿数据 |
| Worker/队列/调度/恢复 | `prospect-worker.ts`、`prospect-scheduler.ts`、`prospect-queue-coordinator.ts` | CRM HostedService、ExtractionJob、租约/游标机制 | 有对应运行基础，验收未完成 | Windows 服务生命周期、宕机恢复、租约过期、重试和重复执行 |
| 超级搜索/深度挖掘/反馈/实时事件 | `prospect-super-search.ts`、`prospect-deep-mining.ts`、`prospect-outcome-feedback.ts`、`prospect-live-events.ts` | CRM SuperSearchPlanner、DeepMiningPolicy、Scorecard 等 | 需逐行为核对 | 策略等价、反馈闭环、事件推送、长任务进度 |

## 3. AI、外联与沟通集成（不能按“已有 AI/Chat 模块”直接判定完成）

| 源项目功能域 | 源项目入口 | 目标项目候选 | 初步结论 | 还需核对 |
|---|---|---|---|---|
| Agent 运行时、任务、记忆、技能、触发器 | `agent-*.ts`、`ai-agent.ts`、`agent-skills/` | BasicApp AI/Workflow/Chat 模块 | 有平台能力候选，但等价性未证明 | Agent 规划执行、工具权限、技能加载、记忆、恢复、触发器和审计 |
| 开发信/外联序列/触达资格 | `outreach-sequences.ts`、`prospect-outreach.ts`、`40-development-email.json` | 目标 CRM/AI/Chat 候选模块 | 迁移状态未确认 | 模板、序列步骤、发送资格、退订/抑制、人工审批和发送结果回写 |
| WhatsApp 独立插件 | `whatsapp-plugin/`（Baileys/Meta Provider、API、UI、加密 AuthState、实时事件、历史导入） | 目标 Chat 模块 | **未发现可直接证明等价的 WhatsApp 插件迁移** | 账号连接、Baileys/Meta Provider、加密凭据、会话/媒体、实时消息、历史导入、路由和部署边界 |
| 企业微信/WeCom 命令与连接器 | `wecom-command-gateway.ts`、`integration-worker/src/drivers/wecom-connector-driver.ts` | BasicApp Chat/Workflow/集成能力 | 需逐项核对 | OAuth/凭据、消息收发、命令网关、回调校验、用户绑定和审计 |
| 客户背景调查/市场机会 | `customer-intelligence.ts`、`market-opportunity-*.ts`、`60-background-research.json` | CRM/AI/知识模块 | 迁移状态未确认 | 背调来源、报告结构、市场机会列表/事实、证据和客户关联 |

## 4. 知识、培训、数据交换与外贸工具

| 源项目功能域 | 源项目入口 | 目标项目候选 | 初步结论 | 还需核对 |
|---|---|---|---|---|
| 知识资料库及审核 | `knowledge-assets-test.ts`、README“资料维护” | AI KnowledgeDocument | 仅有知识文档候选，不能证明资料库全量等价 | 文件类型/附件、版本、审核发布、市场/产品标签、访问权限、复训触发 |
| 在线考试/题库/成绩/补考 | `exam-system-test.ts`、README“销售在线考试系统” | 未发现明确 CRM/考试目标模块 | **未确认迁移；列为高风险待核对项** | 题库、试卷、限时考试、自动判分、成绩统计、补考提醒 |
| 客户/业务数据导入导出 | `mysql-data-import.ts`、前端 dump import、README“导入导出” | SaaS ImportHistory/ExportTask | 平台通用导入导出存在，不代表 CRM 业务导入等价 | CRM 字段映射、去重预览、错误行重试、CSV/XLSX/PDF、字段权限、水印/审批 |
| 名片 OCR | `business-card-ocr.ts`、README“小工具” | 目标 AI/CRM/OCR 候选尚未确认 | **未确认迁移** | 名片识别字段、人工复核、重复检查、确认后创建 Lead |
| 出货/贸易单据 OCR 与导入 | `shipment-ocr.ts`、`trade-document-import.ts` | CRM Shipment/Printing/AI 候选 | 需核对 | 单据识别、字段校验、导入映射、人工确认、订单/出货关联 |
| 海关数据导出/外贸小工具 | `customs-export.ts`、README“小工具” | 未发现直接对应目标入口 | **未确认迁移** | 导出字段/格式、国家和商品筛选、汇率/HS 等工具是否属于本次范围 |
| 报表/仪表盘 | README“报表” | BasicApp SaaS/CRM 查询模块 | 迁移状态未确认 | 销售漏斗、国家分布、跟进健康度、团队排行和权限过滤 |

## 5. 当前明确的工程验收阻塞

1. 后端解决方案 restore/build/test 尚无实际结果。
2. GitHub Actions 对当前分支尚无已观察到的运行结果。
3. CRM AppService 真实 DI/动态代理/UnitOfWork + PostgreSQL 双请求测试尚未完成。
4. 自动获客 Worker 的 PostgreSQL 和 Windows 生命周期验收尚未完成。
5. 数据库升级尚未在空库、既有库和平台/租户库实测。
6. 前端 type-check/test/build/lint 尚未得到本分支的完整结果。
7. Windows 11 拉取、构建、启动和核心业务实机验收尚未完成。
8. PR #1 仍为 Draft，未合并；不得在验收完成前合并或改动 `main`。

## 6. 审计结论与执行规则

- CRM 主业务和自动获客已有大量目标实现，接下来重点是行为差异核对和运行验收，而不是重复造模块。
- 源项目的 Agent、WhatsApp、WeCom、知识资料、考试、OCR、报表、导入导出和外贸工具属于独立功能域。目标仓库存在 AI、Chat、SaaS、Workflow 等平台模块，**不构成这些源功能已经迁移的证据**。
- 优先补齐有明确业务价值且源项目有现成实现的功能域：WhatsApp/沟通、外联序列、CRM 导入导出、名片 OCR、资料/考试、报表。每项先查目标现有能力，再决定复用、适配或迁移。
- 对不适合并入 CRM 核心的独立服务（例如 WhatsApp 插件），应以模块/服务边界迁移，不要把第三方 Provider 和加密凭据逻辑硬塞进 CRM Domain。
- 每一项最终标记为“完成”前，必须具备：目标代码入口、API/页面入口、数据/权限映射、自动化测试和实际运行证据。仅目录存在或源码包含关键词不算完成。
