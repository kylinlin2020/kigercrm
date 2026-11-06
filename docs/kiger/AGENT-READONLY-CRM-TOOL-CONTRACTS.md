# Agent 第一批只读 CRM Tools 契约映射

> 状态：目标 API 源码核对与工具契约；尚未注册为 AI Skill/Tool，也不代表 Agent 已能调用。
> 目标分支：`kiger-dev`。

## 1. 本轮核对结果

已核对目标 CRM 查询服务及应用服务接口：

- `ICrmCustomerQueryService` / `CrmCustomerQueryService`：客户分页查询、客户详情。
- `ICrmLeadQueryService` / `CrmLeadQueryService`：线索分页查询、线索详情。
- `ICrmProspectingAppService` / `CrmProspectingAppService`：Campaign、Strategy、Run、Candidate、Review 等自动获客查询与命令。
- `ICrmOpportunityAppService` 是写入应用服务接口；本轮尚未找到可安全复用的商机只读查询契约，因此不把它加入第一批 Tool。
- `ICrmTaskAppService` 和 `ICrmActivityAppService` 当前列出的接口是写操作；不能把它们伪装成只读待办/活动查询工具。

客户和线索查询服务当前已有 `CrmPermissionCodes.Read`、字段查询安全和 Owner Data Scope。客户/线索详情按记录 Owner 调用 `ResolveAccessibleUsersAsync` 并在不可读时返回 null。分页查询把 Owner Scope 应用到查询条件，再访问 Repository。这些现有检查应保留在 Tool 的服务端调用路径中，不应在 AI 层自行复制或绕过。

自动获客服务中有明确的只读方法：`GetCampaignsAsync`、`GetCampaignAsync`、`GetStrategiesAsync`、`GetCampaignRunsAsync`、`GetRunAsync`、`GetRunCandidatesAsync`、`GetCandidateReviewDetailAsync`。同一服务也包含启动、取消、审核、策略激活和转 Lead 等写操作；第一批只读 Tool 必须采用方法级 allowlist，不能把整个 AppService 暴露给模型。

## 2. 第一批 Tool allowlist

| Tool 名 | 服务端调用目标 | 输入 | 输出 | 风险 |
| --- | --- | --- | --- | --- |
| `crm.customers.search` | `ICrmCustomerQueryService.GetPageAsync` | Keyword、CountryRegion、Industry、分页 | 客户列表页 | ReadOnly |
| `crm.customers.get` | `ICrmCustomerQueryService.GetDetailAsync` | CustomerId | 客户详情或 NotFound/NotVisible | ReadOnly |
| `crm.leads.search` | `ICrmLeadQueryService.GetPageAsync` | Keyword、CountryRegion、Status、分页 | 线索列表页 | ReadOnly |
| `crm.leads.get` | `ICrmLeadQueryService.GetDetailAsync` | LeadId | 线索详情或 NotFound/NotVisible | ReadOnly |
| `prospecting.campaigns.list` | `ICrmProspectingAppService.GetCampaignsAsync` | 无 | 当前作用域 Campaign 列表 | ReadOnly |
| `prospecting.campaigns.get` | `GetCampaignAsync` | CampaignId | Campaign 或 NotFound | ReadOnly |
| `prospecting.runs.list` | `GetCampaignRunsAsync` | CampaignId | Run 列表 | ReadOnly |
| `prospecting.runs.get` | `GetRunAsync` | RunId | Run 或 NotFound | ReadOnly |
| `prospecting.candidates.list` | `GetRunCandidatesAsync` | RunId、可选状态 | Candidate 列表 | ReadOnly |
| `prospecting.candidates.review` | `GetCandidateReviewDetailAsync` | CandidateId | 审核详情 | ReadOnly |

此 allowlist 是建议的工具名称与方法映射，不是当前已存在的注册表。

## 3. 必须保留的服务端边界

1. AI Tool 不接收 TenantId、操作者 UserId 或可任意指定的 OwnerUserId。身份与租户只从当前请求作用域获取。
2. 客户/线索分页必须继续通过 QueryService，保留 `PermissionAuthorize(Read)`、FieldSecurity 和 Owner Data Scope。
3. 客户/线索详情对无权记录返回不可见结果；Agent 不得通过不同错误文案泄露跨范围记录存在性。
4. Prospecting 的 Campaign/Run/Candidate 查询必须调用现有 AppService，不能直接访问 Repository。
5. Tool 只能暴露上述逐项 allowlist 方法；不能把 AppService 的全部公共方法自动转换为模型工具。
6. 对分页参数设置硬上限，拒绝超大页尺寸；关键词和筛选值设置长度限制。
7. 返回 DTO 需要经过字段安全过滤；不得将凭据、Provider Secret、内部异常堆栈或不必要的个人信息塞入模型上下文。
8. Tool 执行必须在具备当前用户、当前租户和动态代理授权的请求 scope 中完成。不能把 scoped AppService 注入现有 singleton `KnowledgeRetrieveSkill`。
9. 只读 Tool 不得创建任务、改变候选状态、批准策略、启动/取消 Run、转化 Lead 或触发任何外部请求。
10. 结果应返回结构化对象和来源类型；Agent 的自然语言总结不应被当作业务执行证据。

## 4. 生命周期与执行器注意事项

现有 `AddAISkills` 将 `IAiSkill` 注册为 Singleton，`KnowledgeRetrieveSkill` 只依赖检索器和当前租户 accessor。CRM 查询服务属于请求级业务服务，带有动态代理授权和数据范围依赖。直接把这些服务构造注入 Singleton Skill 会造成 captive dependency 或绕开应用服务代理。

推荐后续执行器在每次 Tool 调用时创建受控 DI scope，并从该 scope 解析明确 allowlist 的查询接口；调用仍须经过框架动态代理/授权管道。若框架的 `IAiSkill` 注册表无法支持 scoped tool execution，应在 AI 模块实现独立的 scope-aware Tool executor，而不是将查询服务改成 Singleton。

## 5. 第一批测试要求

- Catalog 只允许上述只读 Tool 名称；写方法不在 allowlist。
- 参数边界：负 ID、超大页码/页尺寸、过长关键词、非法 Lead Status。
- 用户 A 查询用户 B 的私有 Customer/Lead 时，返回不可见且不泄露记录存在性。
- 不同 Tenant 的 ID 不得跨租户读取。
- Prospecting 查询只能读取当前用户有权访问的 Campaign/Run/Candidate。
- 同一请求取消令牌必须传递到 AppService。
- 只读调用前后数据库写入计数不变，不产生 Worker、外部 Provider 或邮件发送。
- Tool 输出字段经过字段安全策略，敏感字段不得进入模型上下文。
- 并发请求分别创建独立 DI scope，不共享 DbContext/Repository/UnitOfWork。
- 任何写方法（Create/Update/Review/Start/Cancel/Activate/Convert）均不得被只读 Catalog 暴露。

## 6. 不能提前宣称完成的事项

当前已核对真实服务接口和关键授权/数据范围实现，但尚未新增 Tool executor、Skill 注册、CRM AI 权限、MCP 暴露、前端 Agent 面板或集成测试。需要先确认框架 AI Skill registry 对 scoped execution 的支持方式，再实施第一批代码。
