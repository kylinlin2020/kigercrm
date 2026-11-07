# Haituo 源端功能证据清单（本轮核验）

审计源仓库：`hongshuoguo2-cloud/haituo-crm`，分支 `main`。
目标仓库：`kylinlin2020/kigercrm`，基准分支 `kiger-dev`。

本清单记录本轮从源仓库 README 与 Git tree 中直接确认的源端文件入口，供后续目标端逐项映射。它不把“源端存在”误判为“目标端缺失”，也不把目标端存在同名模块误判为“已迁移”。

## 源端入口清单

| 能力域 | 已核验源端文件/目录 | 迁移审计重点 |
|---|---|---|
| 自动获客核心 | `backend/src/prospect-search-planner.ts`、`prospect-provider-dispatcher.ts`、`prospect-provider-request-ledger.ts`、`prospect-coverage-memory.ts`、`prospect-worker.ts`、`prospect-scheduler.ts`、`prospect-queue-coordinator.ts` | 用户已确认主功能迁移；后续重点验证 Provider 覆盖、异步任务、恢复、反馈闭环及结果一致性 |
| 自动获客扩展 | `prospect-super-search.ts`、`prospect-deep-mining.ts`、`prospect-outcome-feedback.ts`、`prospect-live-events.ts`、`prospect-contact-enrichment.ts`、`prospect-website-discovery.ts` | 逐项确认目标 CRM 的 planner、policy、worker 和实时事件是否语义等价 |
| 外联序列 | `backend/src/outreach-sequences.ts`、`prospect-outreach.ts`、`prospect-outreach-eligibility.ts`、`agent-skills/outreach/SKILL.md` | 序列步骤、退订/抑制、发送资格、审批、发送回执和 CRM 互动记录 |
| Agent/知识 | `backend/src/agent-knowledge.ts`、`backend/src/agent-knowledge-test.ts`、`agent-knowledge/*.json`、`agent-skills/*` | 知识条目、技能定义、任务执行权限、版本和来源引用 |
| WhatsApp | `whatsapp-plugin/src/server/providers/meta-provider.ts`、`baileys-provider.ts`、`provider-manager.ts`、`db/repository.ts`、`security/encryption.ts`、`services/whatsapp-history-import.ts`、`src/web/pages/*` | 独立插件含 Meta/Baileys Provider、凭据加密、历史导入和独立 UI；目标 Chat 内部会话本身不等于 WhatsApp 通道 |
| WhatsApp 运维/测试 | `whatsapp-plugin/docs/ARCHITECTURE.md`、`PRODUCTION_DEPLOYMENT.md`、`tests/meta.integration.test.ts`、`tests/ownership-security.test.ts`、`tests/runtime.production.test.ts` | 源端部署假设与目标租户隔离、密钥管理、Inbox/Outbox、Windows 运行方式的差异 |
| 企业微信 | `backend/src/wecom-command-gateway.ts`、`integration-worker/src/drivers/wecom-connector-driver.ts` | OAuth/回调验签、组织和外部联系人映射、命令网关及消息权限 |
| 考试 | `backend/src/exam-system-test.ts` | 题库、组卷、计时、判分、成绩、补考和权限 |
| 名片 OCR | `backend/src/business-card-ocr.ts`、`business-card-ocr-test.ts` | OCR 识别字段、人工校正、重复检测和 Lead 创建确认 |
| 出货/贸易单据 OCR | `backend/src/shipment-ocr.ts`、`trade-document-import.ts` | 单据抽取、校验、人工确认和订单/出货事务 |
| 知识资料 | `backend/src/knowledge-assets-test.ts` | 资料上传、审核、版本、标签、访问权限和知识索引 |
| 数据导入导出 | `backend/src/mysql-data-import.ts`、前端 dump import 相关实现 | 字段映射、预览、去重、错误行、CSV/XLSX 和导出授权 |
| 市场背调/报表 | `market-opportunity-*.ts`、`customer-intelligence.ts`、README 报表场景 | 报告证据、客户关联、漏斗指标口径、权限与导出 |

## 已确认的架构差异

1. 源端 README 明确说明 WhatsApp 插件是独立服务，含 Baileys/Meta Provider、AES-256-GCM AuthState、Socket.IO、AI 翻译、插件测试和独立部署手册。
2. 源端 README 同时说明该插件的原始部署边界为单租户、单实例、私网部署；目标迁移不能照搬这个边界，必须升级为租户隔离、账号级授权和可恢复的持久 Inbox/Outbox。
3. 目标 Chat 模块已有站内 Conversation/Message/Member/SignalR Hub；这只能作为内部会话和实时推送基础，不能证明外部渠道接入完成。
4. 源端自动获客有独立的 worker、scheduler、queue coordinator、request ledger 和 coverage memory；目标端已有对应 CRM 基础设施，但异步 Provider Run orchestration 仍是当前明确缺口。
5. 源端目录中存在考试、名片 OCR、知识资料和贸易单据导入等独立入口。目标端应逐项确认是否有实际 API/UI/持久化和测试，不能因 AI/CRM/Printing 模块存在而判定完成。

## 下一步核验顺序

- **P0**：异步获客 worker 和父 Run 完成协调；外部发送审批边界；WhatsApp Meta 入站验签、Inbox 幂等和账号路由。
- **P1**：外联序列、企业微信连接器、名片 OCR、CRM 导入导出。
- **P2**：考试、知识资料审核/版本、贸易单据 OCR、市场背调和报表。

## 关闭规则

每一项都需要源端行为、目标端 API/UI/数据/权限映射、异常/并发/恢复测试及运行证据。只记录源文件路径不构成迁移结论。
