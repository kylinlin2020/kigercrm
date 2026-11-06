# 开发信与自动触达序列迁移设计

> 状态：迁移设计与目标差距确认；不是已完成的代码迁移。
> 目标分支：`kiger-dev`。外部邮件发送必须保留人工审批门槛。

## 1. 本轮审计结论

源项目 `haituo-crm` 的 `backend/src/outreach-sequences.ts` 已实现一条独立的触达序列业务链：

- 触达对象限定为当前用户/团队可访问的 Customer 或 Lead。
- 通道为 Email 或 Communication；Communication 当前仅允许已进入客户库的对象。
- 每条序列最多 5 个步骤，每步保存延迟、主题和正文快照。
- 邮件步骤必须具有主题；收件地址从真实 CRM 对象解析，不允许由序列调用方任意伪造。
- 创建前执行 prospect outreach eligibility 检查，并按 approvalStepId 做幂等重放。
- 支持序列列表、暂停、继续和取消。
- Runner 到期后先检查 stopReason，再将步骤标记为 sending、持久化，然后执行发送。
- 服务重启时遇到 sending 状态不确定的步骤会停止序列，避免盲目重发。
- 成功后更新发送步骤、下一次执行时间和任务事件；失败后终止序列并保留错误。
- 回复、退订、资格变化等停止条件由 runtime.stopReason 负责。

源项目的 `agent-skills/outreach/SKILL.md` 同时规定：先读取客户/线索与最近跟进；草稿生成不等于发送；真实发送前必须冻结收件对象、账号、主题和正文并执行外部动作确认；发送成功后回写活动记录；收到回复或退订必须停止后续触达。

目标项目现状：

- CRM 已有 Customer、Contact、Lead、Opportunity、Activity 等业务对象和受权限/租户/数据范围保护的应用服务。
- `SysCrmActivity` 可记录 Email、WhatsApp 等交互，但它是活动流水，不是可调度、可暂停、可恢复的序列状态机。
- SaaS 模块存在 `EmailMessageSender`，可经邮件网关执行 SMTP 发送并写入 `SysEmail`。它属于通用消息基础设施，不提供 CRM 序列的审批、资格检查、退订抑制、步骤幂等和业务关联保障。
- AI 模块目前注册的业务技能是只读知识检索；不能把它视为已具备开发信草稿、序列编排或 CRM 外部发送工具。

因此：开发信与自动触达序列在目标项目中仍是**待迁移能力**。通用邮件发送器和 CRM Activity 只能作为可复用基础设施，不能标记为等价迁移完成。

## 2. 目标架构边界

建议新增 CRM Outreach 子域，而不是把序列逻辑塞进 AI Chat、通用 EmailMessageSender 或 Prospecting Extraction Worker。

### 2.1 领域对象

建议的最小对象：

- `SysCrmOutreachSequence`：租户、负责人、目标实体类型/ID、通道、发件账号引用、状态、当前步骤、下一次执行时间、停止原因、审批快照引用、Row_Version。
- `SysCrmOutreachStep`：序列 ID、步骤序号、延迟、主题/正文快照、状态、计划时间、发送时间、错误摘要、幂等键。
- `SysCrmOutreachSuppression`（或统一抑制记录）：租户、规范化收件地址、通道、原因（unsubscribe/reply/manual/do-not-contact）、来源和时间。
- `SysCrmOutreachDispatch`：一次外部发送尝试的冻结载荷哈希、审批人/时间、幂等键、外部消息 ID、发送状态和不确定结果标记。

所有对象必须具备租户隔离、审计字段、软删除策略和必要的唯一索引。目标实体引用只能通过当前租户 Repository 读取并校验负责人/数据范围，不能仅凭客户端传入 ID 建立序列。

### 2.2 状态机

Sequence：
`Draft → PendingApproval → Approved → Active → Completed`

可控终止/中间状态：
`Paused`、`Cancelled`、`Stopped`、`Failed`。

Step：
`Pending → AwaitingApproval → Sending → Sent`；
异常路径包括 `Skipped`、`Failed`、`DispatchUncertain`。

禁止把 `DispatchUncertain` 自动重置为 Pending。SMTP/Provider 已接受请求但响应丢失时，必须先按外部幂等键或消息记录核对；无法确认则暂停并人工处置，避免重复触达。

## 3. 强制安全与审批不变量

1. **人工审批是硬门槛。** AI 只能生成草稿和建议步骤；不能因用户说“自动跟进”就绕过外部发送审批。每次实际发送前，审批对象必须冻结并绑定：租户、目标客户/线索、收件人、发件账号、通道、主题、正文、附件摘要、计划时间和步骤序号。
2. 冻结载荷的任何字段变化都使原审批失效；变更后重新审批。
3. 只允许发送给当前租户内、当前操作者有权访问的 CRM 对象中已验证的收件地址；禁止跨租户或从不相关对象拼接邮箱。
4. 创建序列时重新执行营销/触达资格检查；发送前再次执行，防止审批后状态变化。
5. 回复、退订、投诉、硬退信、客户要求停止、手动 DNC 或资格失效必须在发送前抑制后续步骤。
6. 每个步骤具有数据库唯一幂等键；并发 Worker 只能认领一次。租约必须有 token/version/expiry fencing，过期 Worker 不得提交结果。
7. 外部发送成功后才记为 Sent，并记录真实 ProviderMessageId；失败、拒绝和不确定结果必须区分。
8. 发送成功后写入 CRM Activity，并将序列步骤、外部发送记录和 Activity 的关联信息保持可追溯。不要把“写入 Activity”当成 SMTP 已发送的证据。
9. 序列取消/暂停必须阻止后续认领；已经进入外部发送边界的动作需要按不确定结果流程处理。
10. 账号密码、SMTP 密钥和 OAuth 凭据不得复制到序列快照、AI Prompt、日志或审计事件。

## 4. 与目标项目现有能力的复用关系

| 能力 | 可复用目标组件 | 仍需补齐 |
| --- | --- | --- |
| 客户/线索与负责人校验 | CRM Customer/Lead Repository、数据范围服务 | 序列专用资格策略与发送前二次校验 |
| 交互记录 | `ICrmActivityAppService` / `SysCrmActivity` | 发送结果与 Activity 的可靠关联 |
| SMTP 发送 | SaaS `IMessageSender` / `EmailMessageSender` | CRM 审批快照、业务幂等、DNC 和发送资格门禁 |
| AI 内容生成 | AI Assistant、Prompt Store | 受控草稿生成契约；AI 不持有直接发送权限 |
| 后台任务 | 目标项目现有后台任务/租约模式（需进一步核对） | Outreach 专属认领、fencing、调度、恢复和死信处理 |
| 权限 | CRM Permission、数据范围、字段安全 | Outreach 专属 Create/Read/Approve/Execute/Cancel 权限 |
| 多语言 UI | 现有前端 locale 与 PageRegistry | 序列列表、编辑、审批、步骤状态、暂停/继续/取消页面 |

## 5. 推荐实施顺序

### Phase A — 只读业务草稿与资格查询
- 提供生成开发信草稿的受控入口。
- 输入必须关联真实 Customer/Lead，并读取产品、市场、最近 Activity 与负责人上下文。
- 输出主题、正文、依据和缺失字段；不调用 SMTP、不创建待发送任务。
- 测试跨租户、越权负责人、无邮箱、缺少业务上下文和提示注入。

### Phase B — 序列数据模型与人工审批
- 新增序列、步骤、审批快照、抑制记录和唯一索引。
- 建立创建/编辑/提交审批/批准/拒绝/暂停/取消 API。
- 审批冻结完整载荷；修改后撤销原批准。
- 只允许创建 Draft/待审批记录，不能在批准之前触发任何外部发送。

### Phase C — 单步发送与审计闭环
- 先实现人工批准的一次性单步发送。
- 通过既有消息抽象发送，使用稳定幂等键并处理 dispatch uncertain。
- 发送成功后写 CRM Activity；记录 ProviderMessageId。
- 在测试环境使用可替代 SMTP 的发送适配器，不连接真实客户邮箱。

### Phase D — 多步骤 Worker
- 增加租约认领、fencing version、过期恢复、退避和死信状态。
- 每一步发送前重新检查审批快照、客户资格、回复/退订和发送窗口。
- 任何审批快照不匹配或不确定发送结果都必须停止自动推进。

### Phase E — 前端、权限与运行验收
- 增加序列列表/详情/编辑/审批/执行历史界面和七种语言资源。
- 验证租户隔离、负责人数据范围、字段安全、权限矩阵、并发 Worker 和数据库迁移。
- 最后才进行受控 SMTP 沙箱验收；真实外发始终要求人工批准。

## 6. 必须补齐的测试矩阵

- 目标不存在、跨租户、负责人不可见、邮箱缺失、无效地址、DNC、退订、已回复。
- 审批后修改收件人/账号/主题/正文/附件/计划时间，必须拒绝执行并要求重新审批。
- 同一审批请求重复提交只能创建一条序列；相同幂等键不同载荷必须冲突。
- 两个独立数据库连接同时认领同一步骤，只能一个成功。
- 租约过期后旧 Worker 的结果写入必须被 fencing 拒绝。
- Provider 成功、Provider 明确失败、请求超时但结果不确定、进程在发送前/发送后崩溃。
- 收到回复/退订与 Worker 同时发生时，发送前资格检查必须阻断后续步骤。
- Pause/Cancel 与到期认领并发时不得启动新的外部动作。
- 成功发送后 Activity 与 ProviderMessageId 可追溯；失败时不得伪造成功 Activity。
- 全部查询、命令和 Worker 均需覆盖 tenant_id 与 owner/data-scope 约束。

## 7. 完成判定

只有在目标代码、数据库迁移、权限/菜单、前端页面、自动化测试和运行证据全部齐备后，才可将“开发信与自动触达序列”标记为迁移完成。

本设计本身不代表已实现任何新的发送 API、Worker、SMTP 流程或 UI。当前仍未执行 .NET restore/build/test、PostgreSQL 迁移/并发验收或 Windows 实机验收。
