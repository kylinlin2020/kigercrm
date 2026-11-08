# Haituo CRM → KIGER CRM 优先迁移缺口台账

> 本台账补充 `HAITUO-CRM-FUNCTION-MIGRATION-MATRIX.md`。它区分“目标仓库存在模块”与“行为等价且验收通过”。当前状态来自目标仓库静态代码审查；未执行的运行时验证不得标记为通过。

## 状态定义

- **代码候选已存在**：目标仓库能定位到实现入口，但尚未完成源/目标行为等价验收。
- **设计完成、实现待做**：已有迁移设计，目标代码尚未证明存在。
- **需源端逐项核对**：需要对照 Haituo 页面、API、权限、数据模型和边界行为。
- **待运行时验收**：代码入口存在，但需 PostgreSQL、并发、外部服务或真实部署验证。

## P0 — 发布前必须闭环

| 能力 | 当前判断 | 必须交付的验收证据 |
|---|---|---|
| 异步 Provider Submit/Poll/Fetch | 已有 Provider 契约、Extraction Job、Repository 和工作动作策略；Run Hosted Service 明确拒绝异步 Provider | 独立 Extraction Worker；Submit 超时幂等恢复；Poll 重试/退避；分页结果原子持久化；父 Run 汇总完成；NeedsReview 人工恢复 |
| PostgreSQL 租约与并发 | 已有 SKIP LOCKED、租约 token、fencing version 相关实现 | 两个 Worker 并发争抢测试；过期租约不可续租；旧 fencing token 写入失败；跨租户不可读取/修改 |
| WhatsApp Meta Cloud API | 已有迁移设计，尚未证明 Meta Webhook/Provider 实现 | 原始请求体验签；账号路由；Inbox 幂等；消息/状态回执映射；Token 安全存储；限流和重试 |
| 外部发送审批 | 系统要求人工审批，但各外部渠道必须逐入口核对 | 所有邮件、WhatsApp、网站发布均有审批记录、操作者、内容快照和审计事件 |

## P1 — 业务能力迁移

| 能力域 | 当前判断 | 重点核对 |
|---|---|---|
| WhatsApp Baileys | 设计中包含，不能视为已实现 | QR/会话认证状态、断线重连、设备注销、协议风险、账号级健康状态 |
| 企业微信 | 需源端逐项核对 | 通讯录同步、客户联系、消息回调、外部联系人映射、应用权限和签名 |
| 名片 OCR | 需源端逐项核对 | 图片上传、OCR 字段置信度、人工校正、重复联系人检测、隐私留存 |
| 考试/测验 | 需源端逐项核对 | 题库、试卷、随机组卷、限时考试、评分、补考、成绩权限 |
| 知识库 | 需源端逐项核对 | 文档解析、切片、向量检索、权限过滤、引用来源、增量索引 |
| 外贸业务工具 | 需源端逐项核对 | 产品/报价/订单/物流、币种和单位、客户跟进及提醒 |
| 报表与看板 | 目标有 CRM 报表候选实现 | 指标口径、筛选范围、导出、时区、租户权限 |

## P2 — 体验和运营完整性

| 能力 | 验收要求 |
|---|---|
| 历史聊天导入 | 预览、字段映射、去重、失败重试和导入审计 |
| 多语言/翻译 | 原文与译文分离；支持人工修订；不得覆盖原始消息 |
| 媒体管理 | 文件授权、下载过期、类型/大小限制、病毒扫描策略 |
| 自动化节奏 | 默认关闭；发送动作受审批和频率限制；支持暂停与审计 |
| 数据迁移工具 | 可重复执行、断点续传、错误报告、回滚或补偿方案 |

## 本轮并行工作分线

- **A 异步获客**：先补齐动作策略测试，再实现独立 Worker 和父 Run 协调；不把现有同步 Run Worker 当作异步完成。
- **B 稳定性**：补齐 Repository 租户/租约/游标契约测试，随后增加 PostgreSQL 真实并发集成测试。
- **C WhatsApp**：先实现 Meta 原始请求体 HMAC-SHA256 验签与单元测试，再做账号路由、Inbox/Outbox 和消息映射。
- **D 迁移审计**：逐项对照源端入口，优先完成 P0/P1 缺口；未验证功能保持“待核对”。

## 关闭规则

只有同时具备以下证据才允许将条目标记为“完成”：

1. 源端功能/API/权限/数据结构已定位；
2. 目标端对应实现和数据库结构已定位；
3. 正常、异常、权限、并发及恢复路径已测试；
4. 自动化测试通过，且必要时完成 PostgreSQL/外部服务集成测试；
5. 实机 Windows 原生部署验收完成。

静态搜索、设计文档、代码存在或一次成功请求，均不足以单独关闭迁移项。

## 2026-11-08 implementation checkpoint

This checkpoint reconciles the gap register against the current four work branches. It is a source review only; it does not upgrade any item to runtime-accepted.

### Current verified source boundaries

- **Async extraction (A):** the durable job entity/repository, PostgreSQL SKIP LOCKED claims, lease token/fencing, submission intent, poll/status/page policies and expiry worker exist. The Run HostedService still preflights and rejects async-only providers before any synchronous provider I/O. No dedicated Submit/Poll/Fetch consumer or durable parent-Run continuation is wired. Keep async providers unavailable to production Run plans.
- **PostgreSQL regressions (B):** the integration-test source covers expired provider-job cleanup, expired submission acknowledgement rejection, expired lease renewal rejection, expired successful final-page completion rejection, and expired incomplete-final-page rejection (the latter added in commit 9052ae05e9d03f42c2712f5e91a348e1c031f5fa). The tests assert that stale workers cannot alter status or accumulated record/failure counts. These tests have not been executed against a live PostgreSQL instance in this environment.
- **WhatsApp Meta (C):** the public GET verification endpoint and POST signature/body/payload validation exist. The POST endpoint intentionally returns HTTP 503 after successful validation because tenant WABA/phone-number binding and atomic durable Inbox insertion are not yet connected. The inbox entity and unique tenant/WABA/message identity index are foundations, not proof of message ingestion. Do not return 200 until durable acceptance is implemented.
- **CSV migration preview (D):** lead/contact/customer preview services validate bounded UTF-8 CSV, source-header mappings, target-field allowlists and required fields. They are side-effect-free previews. There is no confirmed-import command with immutable preview snapshot, explicit confirmation, transactional batch writes, rollback and import audit completion in this branch yet.

### Next implementation gates

1. A: implement a tenant-scoped extraction consumer with independent lease heartbeat, idempotent Submit, bounded Poll, page persistence before cursor advancement, and parent Run continuation. Keep async execution fail-closed until the entire path is covered.
2. B: run the new integration tests against the actual PostgreSQL acceptance database and add two-connection crash/takeover scenarios; do not infer pass from test source.
3. C: add server-owned WABA/phone-number tenant binding, Inbox repository atomic insert/dedupe, and a consumer that projects inbound messages/status receipts. Only then change POST acknowledgement from 503.
4. D: design the confirmed-import transaction against the existing CRM repositories and tenant/permission model; preview must remain read-only and the import must never create a second CRM master-data store.

### Validation status

All four PRs remain Draft and unmerged. No .NET build/test or live PostgreSQL execution is claimed here. Windows-native restore/build/test remains the final acceptance gate.
