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
- **PostgreSQL regressions (B):** the integration-test source covers expired provider-job cleanup, expired submission acknowledgement rejection, expired lease renewal rejection, expired successful final-page completion rejection, expired incomplete-final-page rejection (`9052ae05e9d03f42c2712f5e91a348e1c031f5fa`), and expired cursor-advance rejection (`da55a5915e664f293d79d5a116e55896e2560e1f`). The cursor case asserts that a stale worker cannot advance the cursor or accumulate page counts. These tests have not been executed against a live PostgreSQL instance in this environment.
- **WhatsApp Meta (C):** the public GET verification endpoint and POST signature/body/payload validation exist. The POST endpoint intentionally returns HTTP 503 after successful validation because tenant WABA/phone-number binding and atomic durable Inbox insertion are not yet connected. The inbox entity and unique tenant/WABA/message identity index are foundations, not proof of message ingestion. Do not return 200 until durable acceptance is implemented.
- **CSV migration preview (D):** lead/contact/customer preview services validate bounded UTF-8 CSV, source-header mappings, target-field allowlists and required fields. They are side-effect-free previews. There is no confirmed-import command with immutable preview snapshot, explicit confirmation, transactional batch writes, rollback and import audit completion in this branch yet.

### Next implementation gates

1. A: implement a tenant-scoped extraction consumer with independent lease heartbeat, idempotent Submit, bounded Poll, page persistence before cursor advancement, and parent Run continuation. Keep async execution fail-closed until the entire path is covered.
2. B: run the new integration tests against the actual PostgreSQL acceptance database and add two-connection crash/takeover scenarios; do not infer pass from test source.
3. C: add server-owned WABA/phone-number tenant binding, Inbox repository atomic insert/dedupe, and a consumer that projects inbound messages/status receipts. Only then change POST acknowledgement from 503.
4. D: design the confirmed-import transaction against the existing CRM repositories and tenant/permission model; preview must remain read-only and the import must never create a second CRM master-data store.

### Validation status

All four PRs remain Draft and unmerged. No .NET build/test or live PostgreSQL execution is claimed here. Windows-native restore/build/test remains the final acceptance gate.

### 2026-11-08 WhatsApp inbox schema correction

- Added `EventKey` and `EventType` to the Chat inbox entity. The unique boundary is now tenant + WABA + event key; `ProviderMessageId` is retained for correlation rather than being treated as the sole event identity.
- Added incremental database script `5.9.40`: existing inbox rows are backfilled as `InboundMessage`, then the old message-only unique index is replaced by the event-key index.
- Added entity/migration source contract tests. These are source changes only; the migration has not been executed against PostgreSQL, and no Inbox repository, account binding, or consumer is implemented.
- **Important:** the event-key schema is only a prerequisite. The application must still produce canonical deterministic keys for each message and each status transition, and atomically persist every event before the webhook may return 200. POST remains 503.
### 2026-11-08 additional lease and inbox regressions

- **B PostgreSQL test source:** added `Transition_ExpiredLease_CannotChangeExtractionLifecycle` to prove a worker with an expired lease cannot move an extraction from `Running` to `NeedsReview`. This joins the existing stale-lease tests for acknowledgement, renewal, result completion, incomplete-page commit, and cursor advancement. The test has not been executed against PostgreSQL.
- **C Inbox schema:** the Chat branch now carries event-level `EventKey`/`EventType` and migration `5.9.40`; the old `ProviderMessageId`-only unique index is replaced. Runtime migration and inbox processing remain unverified/unimplemented.
### 2026-11-08 WhatsApp event-key factory increment

- Added a deterministic SHA-256 event-key factory for inbound messages and status receipts. Status keys include the provider message ID, recipient, normalized status, timestamp, and canonicalized error JSON; repeated identical status events deduplicate while later transitions remain distinct.
- Added source tests for deterministic output, same-wamid message/status separation, duplicate receipt handling, later transitions, canonical JSON, and invalid identity rejection.
- The factory is not wired to a durable Inbox writer yet. Account routing, atomic persistence, and provider acknowledgement remain blocked.

### 2026-11-08 WhatsApp event identity regression increment

- Added source tests confirming inbound-message identity normalization trims WABA/phone/message identifiers, while a different `phone_number_id` yields a distinct event key.
- Added a status-key regression asserting materially different error arrays do not collapse into one event identity.
- These tests cover the deterministic key factory only; they do not establish tenant binding, durable inbox insertion, or PostgreSQL behavior. The Meta webhook remains fail-closed with HTTP 503 until the durable acceptance path is implemented and tested.

### 2026-11-08 WhatsApp nested error canonicalization regressions

- Added factory-level tests for recursively reordered properties inside nested status-error arrays; equivalent JSON structures must produce the same event key.
- Added a maximum-depth rejection test for status error JSON, matching the bounded JSON parser configuration.
- These are source-level tests only. No runtime test execution, durable Inbox writer, tenant account binding, or Meta webhook acknowledgement change is claimed.

### 2026-11-08 additional A/B regression checkpoints

- **A async orchestration:** documented that the current async preflight exception is persisted through the generic fenced Run failure path. This is intentionally fail-closed but not resumable; no async Provider is enabled and no parent waiting state is claimed implemented.
- **B extraction scheduling:** added a PostgreSQL integration-test source case proving a job whose `NextPollAtUtc` is in the future must not be claimed or receive a lease. The test is guarded by `KIGER_TEST_POSTGRES_CONNECTION_STRING` and has not been executed against a live database.

### 2026-11-08 CSV preview parser hardening

- Found that the preview parser treated physically blank lines after the first record as empty data rows because it used the prior-record count as a reason to append each line.
- Changed record finalization to skip a physical line only when it contains no field, no cell content, and no explicitly quoted field. Delimiter-only records and quoted empty cells remain real CSV records.
- Added source tests for CRLF inside quoted fields, illegal characters after a closing quote, and trailing blank physical lines.
- The parser/test changes have not been compiled or executed in this environment; Windows-native build/test remains an acceptance gate.

### 2026-11-08 CSV EOF quoted-empty record regression

- A second parser edge case was found during follow-up review: an explicitly quoted empty final field (`""`) without a trailing newline was dropped at EOF because finalization only checked cell text and prior columns.
- EOF finalization now treats `afterQuote` as record content, preserving the explicitly quoted empty row so required-field validation can reject it accurately rather than silently omitting it.
- Added a regression test asserting the final row is counted, retained in preview, and marked invalid when the required CompanyName is empty.
- Source changes are committed; compilation and test execution remain pending on the Windows-native acceptance machine.

### 2026-11-08 CSV physical source row numbering

- Follow-up review found preview diagnostics used logical data-row index (index + 2) as the source row number. This becomes misleading when blank physical lines are ignored or quoted fields span multiple physical lines.
- CSV parsing now retains each logical record's starting physical line number. Preview rows report that source line, while total/valid/invalid counts remain based on logical CSV records.
- Added regression coverage for a blank physical line and a quoted multiline field; expected source line numbers are 3, 4, and 6.
- Changes are committed but remain uncompiled and unexecuted pending Windows-native validation.

### 2026-11-08 CSV carriage-return line diagnostics

- Added a regression case for legacy CSV files that use bare carriage return (`CR`) record separators rather than LF or CRLF.
- The preview parser's physical source-line tracking is expected to report data records on lines 2 and 3 for this fixture. This complements the existing blank-line and quoted-multiline CRLF coverage.
- This is source-level regression coverage only; compilation and execution remain pending the Windows-native acceptance run.

### CSV physical-line regression follow-up — 2026-11-08

Added a focused regression case for a quoted field containing bare-CR (`\r`) line breaks. The fixture verifies that the embedded carriage return remains part of the field value and that the following record is reported at physical source line 4. This complements the existing CR-only record-separator test; source changes are committed, but compilation and test execution remain pending.

 
## 2026-11-08 branch review correction

- **A**: the current Run worker still rejects async-provider plans before synchronous provider execution. Do not add `WaitingForExtractions` as a status-only enum change; parent Run transition and complete child-intent creation must commit atomically, followed by an independently fenced continuation worker.
- **B**: the new PostgreSQL integration suite has 15 cases for claim/tenant isolation/submission idempotency/lease expiry/cursor atomicity. It is gated by `KIGER_TEST_POSTGRES_CONNECTION_STRING` and has not been run against a live database.
- **C**: PR #4 now contains Meta challenge verification, HMAC signature verification, strict inbound/status parsing, event-level SHA-256 identity, Inbox entity and migrations `5.9.39`–`5.9.41`. The endpoint intentionally returns 503 until tenant binding and atomic Inbox persistence exist. The `5.9.41` participant constraint and its migration test were repaired in commit `964dd61`; PostgreSQL execution is still required.
- **D**: CRM Export providers and read-only CSV Preview endpoints exist in this branch. Contact CustomerId preview now validates existence through a bounded tenant-scoped batch lookup. Preview is still not an import; immutable snapshots, human confirmation and transactional writes remain outstanding.
- **No duplicate CRM**: use the target's existing CRM customer/contact/lead/opportunity/order/shipment records; do not introduce a second CRM integration/data model.


### 2026-11-08 contact CustomerId format boundary checkpoint

Added CSV preview regression coverage for CustomerId numeric boundaries: positive Int64 values are accepted, while zero, negative values, explicit plus signs, decimal notation, and Int64 overflow are rejected using invariant-culture NumberStyles.None parsing. This remains format validation only. It does not prove that the referenced customer exists in the current tenant; a bounded tenant-scoped batch lookup is still required before confirmed import. The tests have not been executed in this environment.


### 2026-11-08 tenant-scoped Contact CustomerId preview validation

- Implemented ICrmCustomerLookupRepository backed by the existing SaasRepository<SysCrmCustomer> so the platform's current-tenant and soft-delete query filters remain in force.
- Customer IDs are deduplicated and queried in bounded batches of 500, with a hard request cap of 5,000 distinct IDs. No per-row database lookup is performed.
- Contact preview now parses the full bounded CSV row set (up to 5,000) for relationship validation and aggregate valid/invalid counts, while returning only the first 100 preview rows.
- Missing customer IDs are reported as row-level errors without exposing whether an ID exists in another tenant.
- Added app-service tests for missing tenant customer references and for invalid rows beyond the first 100 preview rows.
- This remains read-only preview validation. Immutable preview snapshots, explicit human confirmation, idempotent transactional import and rollback are still not implemented. Tests have not been executed.


### 2026-11-08 PostgreSQL tenant isolation and bounded lookup tests

Added PostgreSQL-gated repository integration tests for the new contact CustomerId lookup. The tests assert that a customer ID owned by another tenant is not returned under the current tenant scope, duplicate input IDs are collapsed, and 501 existing IDs are retrieved across the repository's 500-ID query boundary. Tests require KIGER_TEST_POSTGRES_CONNECTION_STRING and have not been executed. Preview remains read-only; snapshot confirmation and transactional import are still outstanding.


## 2026-11-08 integration checkpoint — four workstreams merged

The four reviewed workstream PRs have now been merged into `kiger-dev` in sequence:

- PR #2 (async prospecting execution contracts): merge commit `449f97134249461958e62ddab02067112c397948`.
- PR #3 (PostgreSQL extraction regression guards): merge commit `95d6270c95b62dbfffddf92cb17e1a581f83b1f1`.
- PR #4 (WhatsApp Meta webhook verification and event inbox schema): merge commit `d4790351a99dd8144b4693c4d366125028bca92a`.
- PR #5 (CRM import/export preview and module registration cleanup): merge commit `4cd24f4a94a729aa0ed7b63c784192f9e572b7ab`.

The branch integration is complete; feature acceptance is not. The functional gates above remain open: async parent/child continuation and consumer, PostgreSQL execution, WhatsApp tenant binding plus atomic Inbox processing, and confirmed transactional CRM import. The webhook remains fail-closed (503), async providers remain disabled, and CSV remains preview-only. No .NET build/test or live PostgreSQL run is claimed. Do not treat the PR merge status as a migration-completion status.

## 2026-11-15 checkpoint — 自动获客批次 2 W3（批量审核/转换 + 短名单）

本条目记录 `AUTO-PROSPECTING-BATCH-2-PLAN-2026-11-11.md` W3 的交付状态，**不将任何差距项标记为完成**。

- **状态：待运行时验收**（按本文件「状态定义」；未满足关闭规则第 3～5 条的运行时部分）。
- **源端 / 目标端定位（规则 1、2）**：源端 Haituo 候选短名单 + 批量转 Lead 界面；目标端 `SysCrmProspectingCandidate.ShortlistedAt`、升级脚本 `UpdateScripts/5.9.42/5.9.42.sql`、4 个批量 Dynamic API、`frontend/src/modules/crm/views/crm/prospecting/index.vue` 多选与批量动作条。定位完成。
- **自动化测试（规则 4 的静态部分）**：后端 W3 新增测试全绿；`Api.Tests` 全量 1109 总 / 146 失败 / 932 成功 / 31 跳过，与本批次基线逐项一致（零回归）。前端 type-check 0 错误、eslint 0 警告、API 契约单测 4/4、生产构建成功。146 个失败为既有基线债，其中 19 个集中在异步 Worker / Provider 源码契约断言，与 W3 无关。
- **仍未取证**：正常 / 异常 / 权限 / 并发 / 恢复路径的**真实运行**证据；PostgreSQL 并发转换集成测试（`KIGER_TEST_POSTGRES_CONNECTION_STRING` 未配置，相关用例跳过）；升级脚本在空库 / 既有库 / 平台租户库实测；预览 9708 / 9800 的 4 个批量端点真实 HTTP 冒烟；浏览器多选焦点与键盘可达性；Windows 原生部署实机验收（规则 5）。
- **矩阵处置**：`HAITUO-CRM-FUNCTION-MIGRATION-MATRIX.md` 第 2 节第 25 / 26 行保持「已有对应实现 / 还需核对」，不改判为完成。

结论：W3 已具备代码入口与自动化测试，但不得据此关闭迁移条目。

同日后续口径更新：`CrmProspectingApplicationWiringTests` 的 19 个过时源码契约断言已对齐重构后实现并全部转绿（44/44），`Api.Tests` 全量由 1109/146 失败变为 **1109/127 失败、951 通过、31 跳过**，零回归。剩余 127 个失败仍属既有基线债；实机验收与批量端点真实 HTTP 冒烟未完成，本条目状态不变。

同日再后续口径更新：源码契约测试系统性对齐（4 批：`File.Exists` 目录误判 28、注册职责拆分 9、Worker 字面量 3、迁移版本/职责归属 3）后，`Api.Tests` 全量为 **1109 总 / 58 失败 / 1020 通过 / 31 跳过**；失败 127 → 101 → 58，累计清零 69 个既有基线失败，每轮总数与跳过数不变（零回归）。

同日又后续口径更新：剩余 3 个跨文件顺序断言已按职责边界重设计（`PersistNewAsync` 方法体内围栏/insert/commit 的顺序 + Worker 侧窄化 catch/赢家解析/身份映射时机），仅改测试、未动功能代码。`Api.Tests` 全量为 **1109 总 / 55 失败 / 1023 通过 / 31 跳过**；失败 127 → 101 → 58 → 55，累计清零 72 个既有基线失败，每轮总数与跳过数不变（零回归）。剩余 55 个仍属既有基线债。实机验收与批量端点真实 HTTP 冒烟未完成，本条目状态不变。


## 2026-11-15 checkpoint — 测试债收尾后的口径更新（55 → 36 → 2）

同日两批只改测试夹具/守卫的修复完成后，`Api.Tests` 全量为 **1110 总 / 2 失败 / 1077 通过 / 31 跳过**（55 → 36 → 2）。修复覆盖 SQLite 时间偏移、`response.RequestMessage` 桩缺失、关键词断言只看末次请求、以及 CSV/WhatsApp/分页/注册路径等过时夹具。提交 `8e8d39349`、`fa35f776d`（本地；推送受阻见下）。

剩余 2 个失败是**真实字段安全接线缺口**，不是测试漂移：9 个获客实体缺 `GuardQueryAsync` 查询门控，14 个实体缺实体→DTO 映射器。二者都需要产品侧决定「补齐受控查询服务与映射器」或「收窄登记范围」，不得放宽守卫伪造通过。

本条目状态维持「待运行时验收」：真实 HTTP 冒烟、PG 并发、升级脚本实测、Windows 原生部署实机验收均未取证；`HAITUO-CRM-FUNCTION-MIGRATION-MATRIX.md` 第 2 节第 25/26 行不改判。推送状态：`8e8d39349` 已同步；`fa35f776d` 因 GitHub HTTPS 代理/TLS 错误（`schannel: server closed abruptly`、`Empty reply from server`）尚未推送到 `origin/kiger-dev`，需在网络恢复后补推。
