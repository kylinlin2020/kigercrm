# Agent Skills 与业务工具迁移差距审计

> 审计对象：源项目 `hongshuoguo2-cloud/haituo-crm` 的 `agent-skills/`、`agent-knowledge/`；目标项目 `kylinlin2020/kigercrm` 的 AI/CRM/SaaS 模块。
> 状态：源码清单与目标差距审计；不是 Agent 执行器已迁移的证明。
> 分支：`kiger-dev`。

## 1. 结论

源项目 Agent 能力由三层组成：

1. **Skill 层**：自然语言触发、业务流程、完成标准和失败恢复。
2. **Tool 层**：`crm.*`、`prospect.*`、`outreach.*`、`communication.*`、`ui.*`、`api.*` 等可执行工具。
3. **Policy/Knowledge 层**：系统权限边界、实时 API 契约、风险等级、授权策略与完成证据。

目标项目目前有 AI Assistant/Chat、Prompt、Provider、RAG、知识检索技能和 CRM 应用服务，但在目标 AI 模块源码树中没有发现 Agent Skill manifest loader、业务 Tool registry、tool-call executor 或 Agent operation-policy executor。现有 `KnowledgeRetrieveSkill` 是只读检索工具，不是 CRM 业务执行工具。

因此，不能把“AI 聊天可用”“CRM API 已存在”或“知识检索已注册”标记为 Agent 功能迁移完成。缺少的是受权限、租户、审批、幂等和完成证据约束的安全执行层。

## 2. 源项目 Skill 清单与目标映射

| 源 Skill | 主要职责 | 目标已有可复用基础 | 迁移差距 |
| --- | --- | --- | --- |
| `system-overview` | 系统导航、业务对象、权限边界、通用完成标准 | CRM 模块、权限、菜单、AI Chat | 缺少可注入的系统业务上下文与 Agent 操作策略执行器 |
| `customer-lifecycle` | 客户/线索/商机/跟进/待办闭环 | CRM Customer、Lead、Opportunity、Activity 与应用服务 | 缺少按真实业务契约执行的 CRM Agent Tools、对象回读与统一完成证据 |
| `prospecting` | 搜索任务、进度、候选、复核、转线索/客户 | CRM Prospecting Provider、Campaign、Candidate、Worker | 缺少统一 Agent Tool facade；需将已迁移的获客服务安全暴露给 Agent |
| `outreach` | 开发信、Communication、发送、自动跟进序列 | SaaS Email sender、CRM Activity、Chat/Communication 基础 | 缺少草稿、冻结审批、发送授权、回复/退订抑制、序列状态机与 Agent Tool facade |
| `trade-documents` | PI/CI 创建、审批、导出、下载 | 需进一步核对目标贸易单据模块与 API | 不能仅凭通用文档/打印能力判定业务流程等价；需逐项映射真实单据状态与导出证据 |

源项目 `agent-skills/README.md` 还存在 `consultation` Skill 目录，但 README 的“当前内置 Skill”清单没有列出它。需要后续单独审计其启用状态、触发和工具引用，不应直接纳入五个主 Skill 的完成统计。

## 3. 工具引用差距

源项目各 Skill manifest 声明的工具引用：

- System: `ui.navigate`、`ui.open_customer`、`ui.open_lead`、`api.catalog`、`api.read`。
- Customer lifecycle: `crm.search_customers`、`crm.search_leads`、`crm.get_customer_overview`、`crm.get_pipeline_snapshot`、`crm.list_pending_todos`、`crm.create_todo`、`crm.record_customer_followup`、`crm.update_customer_profile`、UI 与通用 API 工具。
- Prospecting: `prospect.preview_search_plan`、`prospect.start_search`、`prospect.get_search_progress`、`prospect.list_candidates`、`prospect.convert_to_lead`、`prospect.convert_to_customer`，以及 UI/API 工具。
- Outreach: `outreach.draft_development_email`、`outreach.send_development_email`、`outreach.send_whatsapp`、序列创建/进度/暂停/继续/取消、Communication Inbox 与 UI/API 工具。
- Trade documents: CRM 查询、`api.catalog/read/write` 与 UI 导航。

本次目标代码树检查只发现 `KnowledgeRetrieveSkill` 作为 `IAiSkill` 注册；未发现上述业务 Tool 名称或 Agent Tool executor 的实现。目标已有应用服务可作为后续适配层调用对象，但不应让模型直接拼装任意 SQL、任意 URL 或任意动态 API 请求。

## 4. 目标项目必须建立的执行边界

### 4.1 Skill 不等于 Tool

- Skill 是静态/版本化的流程指引与触发元数据。
- Tool 是服务端注册的强类型能力，参数必须经过 schema 验证。
- 模型只能请求已注册 Tool；不得根据 Skill 文本创建新工具或扩大权限。
- Tool 的租户、当前用户、数据范围和权限必须由服务端上下文确定，不能信任模型传入的 tenant/user/owner ID。

### 4.2 只读与写入分层

建议将工具风险明确分为：

- `ReadOnly`：查询客户、线索、商机、任务进度、知识片段。
- `InternalWrite`：创建待办、记录跟进、创建草稿、更新允许的 CRM 字段。仍需服务端权限、租户和数据范围校验。
- `ApprovalRequired`：真实邮件/Communication 发送、自动序列启动、贸易单据审批/导出等具有外部影响或业务审批要求的动作。
- `HighImpactConfirmation`：删除、批量破坏、客户释放、丢单关闭等高影响操作。

模型不得降低 Tool 的风险级别。用户自然语言授权不能替代系统中依法/依角色需要的审批。

### 4.3 外部动作冻结审批

所有外部发送 Tool 必须使用服务端生成的冻结载荷快照，至少绑定：

- TenantId、操作者、目标 CRM 实体类型和 ID；
- 收件人、发件账号引用、通道；
- 主题、正文、附件摘要；
- 发送时间、步骤序号、幂等键；
- 载荷哈希、审批人、审批时间。

任一冻结字段变化必须撤销原审批。AI 只可生成草稿；不得持有绕过审批的 SMTP/WhatsApp 凭据或直接发送权限。

### 4.4 完成证据

每个 Tool 定义完成证据类型：

- 查询：返回的真实对象/任务/记录及其来源。
- 创建：服务端对象 ID，必要时回读核验。
- 更新：回读后的关键字段。
- 外部发送：Provider 成功响应、ProviderMessageId 和 CRM Activity 关联记录。
- 导出：单据 ID、审批状态、导出任务 ID 与文件标识。

没有对应证据，不允许 Agent 声称任务完成。模型生成的自然语言不能作为执行成功凭证。

## 5. 推荐实现路线

### Phase A — Tool Catalog 与只读 CRM Tools
- 建立服务端静态 Tool Catalog：名称、版本、描述、参数 schema、风险等级、权限码、执行器、完成证据。
- 第一批只实现 Customer/Lead/Opportunity/Todo/Activity/Prospecting 的只读工具。
- 每个工具从当前用户和租户上下文取身份，不接受模型覆盖。
- 测试越权、跨租户、数据范围、参数超限和不存在对象。

### Phase B — 受控站内写工具
- 加入创建待办、记录跟进、创建/更新允许字段等工具。
- 复用既有 CRM Application Service，不绕开 Domain Service、UoW、幂等和审计。
- 写入后回读并返回结构化完成证据。

### Phase C — Skill manifests 与版本治理
- 为 Skill 建立机器可读 manifest 和 Markdown 指引。
- 加载器只接受已部署、已审核的 Skill 版本。
- manifest 中 ToolRefs 必须全部存在；未知引用在启动/测试阶段 fail-fast。
- Skill 不能声明权限授予；权限只来自服务端 Permission/Policy。

### Phase D — 审批工具与外部执行
- 先接开发信草稿，再实现人工批准的一次性发送。
- 最后才开放多步骤序列执行；必须集成回复/退订/DNC 检查、幂等和 Worker fencing。
- 每次执行前重新校验冻结载荷、资格、账号授权和审批有效性。

### Phase E — 前端、审计与验收
- 提供 Agent 任务轨迹、Tool 调用记录、审批记录、失败原因与重试入口。
- 验证跨租户隔离、权限、提示注入、工具参数污染、并发、幂等和失败恢复。
- 所有真实外部动作使用沙箱账号验收；生产外发仍需人工审批。

## 6. 必须建立的自动化测试

1. 所有 Skill manifest 的 ToolRefs 必须能在 Catalog 中解析；未知工具引用拒绝加载。
2. Skill 文本不能新增权限码、覆盖风险等级或更改审批策略。
3. Agent 传入其他 TenantId/UserId/OwnerUserId 不得改变服务端身份范围。
4. ReadOnly Tool 不得写数据库、发消息或创建任务。
5. InternalWrite Tool 必须通过应用服务并验证租户、数据范围和权限。
6. ApprovalRequired Tool 缺审批、审批过期或冻结载荷哈希变化时必须拒绝。
7. 外部发送重复请求使用同一幂等键不能重复跨越 Provider 边界。
8. Provider 结果不确定时不得自动重发。
9. 工具执行结果缺少完成证据时，Agent 不能输出成功状态。
10. 日志与模型上下文不得包含 SMTP 密码、OAuth token、Provider secret 或个人账号绑定凭据。

## 7. 当前完成状态

| 工作项 | 状态 |
| --- | --- |
| 源项目五个主 Skill manifest/流程盘点 | 已完成源码审阅 |
| 源项目系统操作与权限契约盘点 | 已完成源码审阅 |
| 目标 AI Chat/RAG/Skill 注册入口审阅 | 已完成源码审阅 |
| 目标 CRM/SaaS 可复用基础盘点 | 初步完成，需继续逐个契约核对 |
| Agent Tool Catalog / executor | 未发现，待实现 |
| CRM 业务只读 Agent Tools | 未发现，待实现 |
| 站内写 Agent Tools | 未发现，待实现 |
| Skill manifest loader / validator | 未发现，待实现 |
| 外部动作审批执行器 | 未发现等价 Agent 层实现，待实现/复用现有授权框架 |
| Agent UI/任务轨迹 | 未发现等价业务 Agent 轨迹界面，待实现 |

本文件是审计交付，不代表任何新 Tool、Skill loader 或 Agent executor 已经落地。目标代码仍需通过 .NET restore/build/test、PostgreSQL 集成测试与 Windows 实机验收。
