# WhatsApp 插件迁移设计与目标 Chat 集成边界

状态：完成静态架构审计，尚未迁移实现。审计源为 hongshuoguo2-cloud/haituo-crm 的 whatsapp-plugin；目标为 kylinlin2020/kigercrm 的 Chat 模块。

## 1. 结论

源插件不是单纯聊天 UI，而是独立 Node.js 服务，包含 Provider Manager、Baileys、Meta Cloud API、账号/联系人/会话/消息存储、Socket.IO、AES-256-GCM 凭据加密、媒体存储、AI 翻译、会话智能、自动化节奏、历史聊天导入、CRM 身份中间件及独立部署流程。

源插件自身明确定位为单租户、单实例、私网部署的生产候选版；CRM Adapter 在架构图中标注为未来接入/未实现；没有完整组织级 RBAC、tenant_id 隔离或多实例协调。迁移时不能把这些限制照搬。

目标 Chat 已有内部 Conversation、Message、Member、Reaction、SignalR Hub、实时推送、权限、审计和保留清理。它们可作为内部会话基础，但不代表 WhatsApp Provider、账号、Webhook、外部联系人、消息同步、媒体或凭据能力已经迁移。

## 2. 能力映射

| 源插件能力 | 目标候选 | 结论 |
|---|---|---|
| 会话/消息/成员/反应 | Chat Domain | 可复用部分模型；需外部渠道绑定和外部 ID |
| Socket.IO | BasicAppChatHub / ChatRealtimePushService | 迁移到 SignalR，不迁移 Socket.IO |
| Provider Manager / ChannelProvider | 未发现等价 Provider 抽象 | 新建独立 Channel Infrastructure |
| Baileys 扫码、AuthState、重连 | 未发现 | 需单独迁移并评估非官方协议风险 |
| Meta Webhook、模板、回执 | 未发现 | 需单独迁移、验签和幂等 |
| WhatsApp 账号和外部联系人 | Chat 成员不能替代 | 新增渠道账号和外部联系人映射 |
| AuthState/Meta Token 加密 | 未发现渠道凭据等价物 | 复用安全密钥策略，禁止明文 |
| 媒体/保留策略 | 尚未证明覆盖 | 对接文件存储和授权策略 |
| 翻译/会话智能 | AI/Chat 通用能力 | 可复用模型治理，但需原文/译文分离 |
| 历史聊天导入 | 未发现 | 新增预览、去重、导入工作流 |
| CRM 客户/待办/互动 Adapter | CRM Activity/Task/Customer | 通过应用层契约接入，不允许 Provider 直连 CRM Repository |

## 3. 推荐架构和依赖边界

```text
WhatsApp Web / Meta Cloud API
  -> Channel Providers (Baileys / Meta)
  -> Channel Application Adapter
       - webhook verification / account routing
       - inbound-outbound idempotency
       - external contact mapping
       - message normalization
  -> Chat Application Contracts
       -> Chat Domain / repositories
       -> SignalR realtime (authorized members only)
       -> CRM Activity/Task/Customer application contracts
       -> AI translation / conversation intelligence
  Side services: Credential Vault; Media Storage; persistent Inbox/Outbox
```

必须遵守：

- Provider 不引用 CRM Domain 实体或 Repository；Chat 不引用 Baileys/Meta SDK。
- Webhook 先验签、确定账号归属、持久化 Inbox 和幂等键，再异步处理。
- 出站使用持久 Outbox；外部消息 ID 和状态回执有数据库唯一约束。
- 实时推送按租户、会话成员和权限过滤，禁止源插件的全局广播方式。
- 外部联系人手机号只能作为匹配候选，跨租户不得自动合并。
- 凭据使用目标系统密钥管理、备份和轮换机制，不照搬开发环境本地 master key。
- 进程 readiness 与每个渠道账号的连接状态分开。

## 4. 分阶段迁移

### Phase A：目标契约和数据模型
定义 ChannelAccount、ChannelCredential、ExternalContact、ChannelConversationBinding、ExternalMessageBinding；统一消息命令和状态；定义租户/账号/会话授权；持久 Inbox/Outbox；凭据加密与轮换；媒体存储和保留。

### Phase B：Meta 官方通道
实现 Webhook challenge/签名、多账号 phone_number_id 路由、入站消息标准化、模板发送、24 小时服务窗口规则、状态回执、重复投递/乱序/重试；完成真实 Meta 测试号码验收。

### Phase C：Baileys 通道
实现扫码/配对、加密 AuthState、连接状态机、退避重连、联系人同步、消息/媒体/状态；锁定版本并提供协议变化诊断和重新扫码运维。明确非官方协议及账号风控风险，不承诺 SLA。

### Phase D：Chat/CRM/AI
外部消息进入 Chat 会话并关联 CRM 联系人；页面显示渠道和账号；SignalR 授权推送；译文与原文分离；客户互动和待办通过 CRM 应用服务写入；历史记录先预览、去重、确认。

### Phase E：安全和运行验收
两租户/两账号隔离、跨账号拒绝、Webhook 签名和重放、重复/乱序消息、并发出站、崩溃恢复、密钥备份恢复、媒体越权、真实 Meta 与 Baileys 账号、Windows 启停升级回滚、日志脱敏。

## 5. 验收完成定义

只有 Meta/Baileys 账号可创建授权断开恢复；消息/媒体/回执/联系人映射正确；Webhook 重放不重复写入；原文不被译文覆盖；SignalR 按租户/成员授权；CRM 写入走应用层；凭据和媒体安全；数据库迁移、备份恢复、Windows 运维及真实渠道端到端测试均有证据，才可标记 WhatsApp 迁移完成。源码静态断言不能替代真实 Provider 测试。

## 6. 本轮结果与下一步

已静态审阅源插件架构、部署手册、API 入口、CRM 身份中间件、Provider 类型、Baileys/Meta Provider、历史导入和 AES-GCM 加密实现；已确认目标 Chat 有内部会话/消息/SignalR 基础设施。尚未新增目标 Provider 或数据库实体。本轮先冻结正确集成边界；下一步检查目标租户、文件存储、后台任务、加密配置和 Chat 授权扩展点，再实现可独立测试的 Phase A 契约。
