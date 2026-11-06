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


## 7. 进一步源码核验：DI 注册与非 HTTP 调用的字段安全

本轮继续读取了 AI 模块服务注册、IFieldSecurityService、FieldSecurityService 和 CRM 查询实现，确认以下实现级约束：

- AddAISkills() 当前明确执行 services.AddSingleton<IAiSkill, KnowledgeRetrieveSkill>()；框架 DefaultAiSkillRegistry 构造时收纳这些 Singleton Skill。当前没有证据表明该注册表支持每次工具调用创建 scoped DI scope。
- IFieldSecurityService 的契约明确区分 GuardQueryAsync 与 MaskAsync：前者防止受保护字段被用于过滤、排序和关键词检索；后者对返回 DTO 对象图执行字段脱敏。
- CRM Customer / Lead / Opportunity / Task 分页 QueryService 显式执行 GuardQueryAsync，并应用 Owner/Assignee Data Scope。详情查询显式执行记录 Owner/Assignee 范围判断。
- 字段脱敏的统一入口是 HTTP FieldSecurityResponseFilter。如果未来 Agent Tool executor 在服务端直接调用 QueryService/AppService，而不是经 HTTP 响应管道，不能假设该 Filter 会执行；必须在 Tool 返回模型上下文之前，对本次调用独占的 DTO 结果显式执行 IFieldSecurityService.MaskAsync。不能对缓存共享对象原地脱敏。
- Customer / Lead / Opportunity / Task 详情方法本身没有直接调用 MaskAsync；其安全依赖 HTTP 响应 Filter。直接调用路径若遗漏显式脱敏，会形成字段级信息泄露风险。
- Prospecting 查询方法有 CrmPermissionCodes.Read 与 Campaign Owner Data Scope 检查；Run/Candidate 读取先校验 Campaign 可访问性。但同样不能把 HTTP Filter 的脱敏效果假定为直接 Tool 调用的一部分。

### 7.1 受控 Tool 执行路径（实施前置条件）

1. 模型只能选择静态 Tool Catalog 中的工具名和经过 schema 校验的参数。
2. 每次调用创建独立 IServiceScope，从该 scope 解析明确的 CRM 查询接口与 IFieldSecurityService。
3. 必须确认框架动态代理授权在从 scope 解析应用服务时仍然生效；不能直接 new QueryService，也不能直接调用 Repository。
4. 应用服务调用结束后，针对返回的 DTO / 分页结果调用 MaskAsync，然后才允许序列化为模型 Tool result。
5. Tool executor 不接受模型传入 TenantId、CurrentUserId、OwnerUserId；所有主体上下文必须来自可信请求/对话授权上下文。
6. Tool 的授权失败、记录不可见、记录不存在采用不泄露存在性的统一对外语义；内部审计可记录真实拒绝原因。
7. 不得把工具结果或 DTO 实例跨请求缓存；每次执行使用独立 scope，避免 DbContext、UnitOfWork、当前用户和字段规则串用。

### 7.2 本轮确认的阻断项

- 框架 DefaultAiSkillRegistry / IAiSkill 是否允许 Skill 内部通过受控 scope factory 执行 scoped 业务工具，尚未从框架源码核实；当前只能确认 BasicApp 注册层为 Singleton。
- 框架动态 API 授权拦截器对“由 Tool executor 在 scope 内解析后直接调用”的接口是否完整生效，必须通过真实 DI/代理集成测试确认。
- HTTP 响应脱敏 Filter 不会自动成为直接方法调用的安全边界；Tool executor 必须显式调用 MaskAsync 并有测试证明。
- 目前没有实现任何 CRM Agent Tool、Tool Catalog 或 MCP 工具注册；本文仍是源码核验和实现约束，不是功能已上线的声明。
