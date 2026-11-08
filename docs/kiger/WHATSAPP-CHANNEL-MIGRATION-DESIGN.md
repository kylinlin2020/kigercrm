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

## 6. 当前实现状态（2026-11-08）

已在目标 Chat 模块增加 Meta Webhook 的 GET challenge 与 POST 验签边界，并增加严格 UTF-8 解码、入站消息解析、状态回执解析和对应契约测试。POST 对签名及结构均有效的事件仍返回 503：这不是遗漏的成功路径，而是有意的 fail-closed 行为，因为 WABA/phone_number_id 租户绑定和 Inbox 原子持久化尚未接通。

数据库侧并非“没有 Inbox 表”：升级脚本 `5.9.39/5.9.39.sql` 已为存量库创建 `sys_whatsapp_webhook_inbox`，含 tenant/WABA/provider-message 唯一索引、状态约束及待处理索引；实体 `SysWhatsAppWebhookInbox` 声明了对应的严格租户实体与唯一索引。脚本说明新库由实体 CodeFirst 建表。该表结构具备持久 Inbox 的基础，但当前尚未发现 Inbox repository、账号绑定实体/查询契约或消费 Worker，故“表存在”不能等同于“Webhook 已可靠入箱”。

Chat 模块的 `ServiceCollectionExtensions` 当前只登记 Chat domain service、seeders 和事件处理器；CRM 的 `AddCrmFieldSecurityEntities` / SaaS 的 `AddSaasFieldSecurityEntities` 是字段安全实体登记，不应被误认为数据库实体发现机制。Inbox 的存量表创建由 5.9.39 SQL 明确保证；新库 CodeFirst 的实体发现路径仍需在框架初始化链中验证，不应通过把 Inbox 粗略加入字段安全白名单来替代。

### 已有 / 未有

| 能力 | 当前状态 |
|---|---|
| Meta GET challenge、POST HMAC 验签 | 已有边界实现 |
| 严格 UTF-8、入站消息与状态回执解析 | 已有解析器及源码测试 |
| Inbox 实体与存量库迁移脚本 5.9.39 | 已有 |
| WABA/phone_number_id -> tenant/account 绑定 | 未实现 |
| Webhook 入站消息与回执的原子幂等入箱 | 未实现 |
| Inbox lease claim、消费、重试与 NeedsReview | 未实现 |
| 入站消息标准化并写入 Chat | 未实现 |
| 状态回执持久化并回写外发消息 | 未实现 |
| Meta Graph API 持久 Outbox、发送审批 | 未实现 |
| 凭据 Vault/轮换、速率限制、真实号码 E2E | 未实现 |

### 下一步实现顺序

1. 明确租户级 Meta Channel Account 模型，唯一绑定 WABA ID 与 phone_number_id；路由只读服务端配置，不信任 webhook 自带 tenant 标识。
2. 为每条 inbound message 和 status receipt 设计 Inbox 记录与唯一幂等身份；考虑同一 webhook envelope 包含多条消息/回执，避免“一个 envelope 一个 ID”的错误模型。
3. 在一个事务中完成 account binding 验证和 Inbox 插入；重复投递在独立事务/安全冲突处理路径中识别为已接收，不能在 PostgreSQL 唯一冲突后复用已中止事务。
4. 仅在所有事件均持久入箱或确认重复后 ACK；任何未路由/未持久事件继续返回可重试错误。
5. 再实现租约消费、Chat 投影、状态回执、Outbox 和人工审批，最后接真实 Meta 测试号码。

Meta/Baileys 的完整迁移仍未完成。源码测试与静态审计不能替代 PostgreSQL 并发测试和真实 Provider 端到端验收。

### 2026-11-08 inbox idempotency decision

A single Meta webhook envelope may contain multiple messages and/or status receipts. A single `ProviderMessageId` column is not a sufficient idempotency key for the envelope or for all event items:

- The same envelope can contain several different `wamid` values and must be split into independently durable event rows.
- A status receipt refers to the same `wamid` as the original inbound message; a unique key on `(TenantId, WABA, ProviderMessageId)` would incorrectly reject the receipt.
- A message may receive multiple legitimate status transitions (for example sent, delivered, read); deduplicating only by `wamid` would lose later transitions.
- Retrying the same webhook must not create duplicate event rows, while a later status transition must remain admissible.

**Required persistence shape before changing POST from 503:**

1. Parse and normalize each message and each status receipt as an independent inbox event. Preserve the verified original envelope for forensic replay, plus the individual normalized event payload.
2. Add a server-generated deterministic `EventKey` (SHA-256 hex) and a bounded `EventType` discriminator. The unique idempotency boundary is `(TenantId, WhatsAppBusinessAccountId, EventKey)`, not `ProviderMessageId` alone.
3. For inbound messages, derive the key from the event type and stable provider message identity (`wamid`). For status receipts, include event type, `wamid`, status value, provider timestamp, recipient identity, and normalized error identity/details when present. Canonicalize fields before hashing; do not hash arbitrary JSON property order.
4. Keep `ProviderMessageId` as a query/correlation field, not the sole unique key. A duplicate key is an acknowledged duplicate only after the winning row is visible outside the failed PostgreSQL transaction; never query in a transaction aborted by a unique violation.
5. Insert all event rows from one verified envelope in a single transaction. Return 200 only if every event row is committed or already exists by the deterministic key. Unknown WABA/phone binding, malformed event identity, or any failed insert remains retryable and must not be acknowledged.
6. Add tests for a multi-message envelope, message plus status for the same `wamid`, repeated identical status, later status transition, two concurrent deliveries, and partial transaction failure rollback.

This is a design gate, not an implementation claim. The current inbox entity's unique index on `(TenantId, WABA, ProviderMessageId)` must be revised before it can safely store both messages and status receipts.
### 2026-11-08 schema implementation checkpoint

The inbox schema prerequisite is now implemented in source:

- `SysWhatsAppWebhookInbox` has `EventKey` (up to 64 hex characters) and `EventType`; the unique index is `(TenantId, WhatsAppBusinessAccountId, EventKey)`. `ProviderMessageId` remains a non-unique correlation field.
- Incremental migration `5.9.40` adds the columns, backfills pre-existing rows as `InboundMessage`, makes the columns non-null, and replaces the prior message-only unique index.
- Entity and migration contract tests were updated to assert the new model and migration shape.

**Not implemented:** canonical event-key generation, WABA/phone tenant binding, Inbox repository, atomic multi-event insert, duplicate-conflict reconciliation, Inbox consumer, Chat projection, or status receipt processing. The SHA-256 canonicalization rules above remain the application contract. Webhook POST must continue returning 503 until these runtime paths and PostgreSQL integration tests are complete.
### 2026-11-08 deterministic event-key implementation increment

- Added `WhatsAppMetaWebhookEventKeyFactory`: inbound message keys use a length-prefixed SHA-256 identity tuple; status receipt keys include WABA, phone, wamid, recipient, normalized status, provider timestamp, and canonicalized error JSON.
- Object properties in error JSON are sorted before hashing; arrays preserve order. Raw `StatusJson` formatting is not part of the key, so whitespace/property-order differences do not create duplicate status events.
- Added source tests for deterministic SHA-256 output, message-vs-status separation for the same wamid, duplicate status delivery, later status transitions, canonical error JSON, and missing identity rejection.
- This factory is not yet called by an Inbox writer because tenant account binding and atomic persistence are still absent. Do not acknowledge webhook POSTs yet.