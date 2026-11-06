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


## 8. 上游框架调用链核验与版本边界（本轮）

本轮继续检查了公开上游仓库 XiHanFun/XiHan.Framework 的 AI Abstractions 源码，并与本项目实际依赖版本对照：

### 8.1 已在上游当前源码确认的机制

- 上游当前源码的 AIToolCatalog 是 Singleton，但持有 IServiceScopeFactory 与 IOperationInvoker；Bind(InvocationContext) 为每个调用方生成绑定工具。
- OperationAIFunction.InvokeCoreAsync 每次调用创建新的 AsyncServiceScope，再创建新的 Operation Context；复制调用方 TraceId、Deadline、Baggage 与 Security Context，然后经 IOperationInvoker.InvokeAsync 执行完整 Operation 管道。
- 上游源码明确说明 Operation 管道的 Authorize 阶段按 Operation Contract 判定；工具不复制凭据、不重新认证，而是沿用已认证调用方的主体与租户安全上下文。
- 这比在 IAiSkill 内直接解析并调用 CRM AppService 更适合作为安全边界：工具参数先序列化为 OperationRequest，服务端由 Operation Registry/Invoker 执行，而不是模型直接选择任意服务或 Repository。

### 8.2 不能直接移植到本项目的原因

- 本项目 backend/src/framework/XiHan.BasicApp.Core/XiHan.BasicApp.Core.csproj 实际引用 XiHan.Framework.AI 与 XiHan.Framework.AI.Abstractions 4.8.0；Web.Core 同样引用 XiHan.Framework.Web.Mcp 4.8.0。
- 本项目 AI 模块当前仍使用 IAiSkill、DefaultAiSkillRegistry 和 KnowledgeRetrieveSkill.AsFunction() 这条接口；本轮目标仓库源码未显示已接入 AIToolCatalog / OperationAIFunction。
- 本轮能读取到的上游 XiHan.Framework 当前 main 源码使用新的 AI Tool Catalog / OperationAIFunction API，并标注 Preview 5.0。没有取得与本项目 4.8.0 依赖精确匹配的上游源码版本，因此不能把 5.0 的 API、生命周期和授权保证假定为 4.8.0 已具备。

### 8.3 实施决策

1. 不在当前 4.8.0 依赖上直接引用 AIToolCatalog、OperationAIFunction 或 Operation Registry 新 API；避免版本不匹配导致编译失败或绕开旧版框架的实际安全管道。
2. 下一步先取得与 4.8.0 精确匹配的 Framework 源码/包符号，确认 IAiSkill、DefaultAiSkillRegistry、MCP 工具绑定、InvocationContext、安全主体传递和授权拦截的真实实现。
3. 若 4.8.0 已有等价 Operation 管道，优先按旧版真实 API 注册只读 CRM Operation；若没有，则先在 BasicApp 内补齐经审查的受控调用桥接与集成测试，再接入模型工具目录。
4. 第一批工具只读；返回结果必须在序列化前显式执行字段脱敏。授权上下文只能继承已认证调用方，禁止模型传入租户、用户或 Owner 身份字段。
5. 在完成 4.8.0 版本匹配验证之前，不宣称已经实现 Agent CRM Tools，也不将上游 5.0 代码视为当前项目可用能力。


## 9. Framework v4.8.0 精确版本源码核验（已解除版本猜测）

本轮通过上游 v4.8.0 发布提交 `f15bf18` 读取了与本项目 NuGet 4.8.0 对应的源码，不再以当前 main / Preview 5.0 推断旧版行为。

核验文件：

- `framework/src/XiHan.Framework.AI.Abstractions/Skills/IAiSkill.cs`
- `framework/src/XiHan.Framework.AI.Abstractions/Skills/IAiSkillRegistry.cs`
- `framework/src/XiHan.Framework.AI/Skills/DefaultAiSkillRegistry.cs`
- `framework/src/XiHan.Framework.AI/Mcp/SkillMcpToolsConfigurator.cs`
- `framework/test/XiHan.Framework.AI.Tests/SkillMcpToolsConfiguratorTests.cs`
- `framework/test/XiHan.Framework.Web.Mcp.Tests/Extensions/McpSkillProjectionTests.cs`
- `framework/test/XiHan.Framework.Web.Mcp.Tests/Extensions/McpDuplicateToolNameTests.cs`

### 9.1 v4.8.0 已确认事实

- `IAiSkill` 仅暴露 `Name`、`Description`、`AsFunction()`；没有 `InvocationContext`、调用者参数或请求 Scope 参数。
- `IAiSkillRegistry` 只负责注册、枚举和按名查找；`DefaultAiSkillRegistry` 在构造时收纳 DI 中的 `IAiSkill`，使用线程安全字典，同名覆盖。
- `SkillMcpToolsConfigurator` 遍历注册表，对每个 Skill 执行 `AsFunction()`，再调用 `McpServerTool.Create()` 加入 MCP ToolCollection。工具集合已存在同名工具时启动配置失败并指出冲突双方。
- Framework v4.8.0 的测试确认已注册 Skill 可通过 MCP `tools/list` 暴露，也可经 `tools/call` 执行；这证明 MCP 通道直接使用 Skill 的 AIFunction，不是 CRM AppService Operation 的自动代理。
- 以上源码没有显示 Skill→MCP 投影过程为每次调用自动创建 CRM DI Scope，也没有显示它会自动复制 CRM 当前用户/租户安全上下文。不能把 v5 Preview 的 `OperationAIFunction` 调用管道套用到 v4.8.0 Skill。

### 9.2 对本项目的直接影响

当前 `KnowledgeRetrieveSkill` 采用 Singleton 注册，`AsFunction()` 是在 Skill 实例上生成的函数。若按相同模式实现 CRM 查询 Skill，并在构造时注入 Scoped CRM 服务，会产生 Singleton 捕获 Scoped 依赖的生命周期问题；若直接用 Repository 查询，则会绕过 CRM AppService 的授权和 Data Scope。

此外，MCP 工具调用并不等同于用户登录的 CRM HTTP 请求。即使 MCP Server 自身配置了 API Key，也不能据此推断 MCP 调用已经绑定某个 CRM TenantId、CurrentUserId、Owner Scope 或字段安全策略。必须核实 MCP 认证如何映射到 CRM 主体，不能把外部客户端身份当作 CRM 用户身份。

### 9.3 当前可接受的实施边界

- 不新增任何持有 Scoped CRM 服务的 Singleton Skill。
- 不在 Skill 中直接访问 Repository / DbContext。
- 不接受模型提供 TenantId、UserId、OwnerUserId 或权限参数。
- 不把 MCP API Key 本身映射成 CRM 用户身份，除非存在显式、可审计且可撤销的服务身份映射与权限策略。
- 继续保留当前 KnowledgeRetrieveSkill 只负责知识检索的边界。
- 第一批 CRM Agent 工具应通过独立的认证/授权上下文适配层，创建每次调用独立的 DI Scope，并在调用 CRM AppService 的授权代理前建立可信的主体上下文；结果在返回模型前显式 Mask。
- 若 v4.8.0 MCP 请求上下文无法安全承载 CRM 主体，则首批工具先只在已登录 CRM Chat 请求内启用，不对外部 MCP 暴露；MCP 暴露另行完成身份绑定设计与集成测试。

### 9.4 下一步实现前必须验证的测试

1. Skill 单例实例被并发调用时，每次执行创建独立 Scope / DbContext / UnitOfWork。
2. 已登录 CRM 用户发起 Chat Tool Call 时，调用者 TenantId / UserId 能可信传入 CRM AppService 授权代理。
3. MCP API Key 调用在没有显式 CRM 主体绑定时默认拒绝 CRM 数据工具，而不是使用系统管理员、匿名或默认租户身份。
4. 两个租户、两个用户并发调用时，数据范围与字段 Mask 互不串用。
5. 越权记录读取不泄露记录存在性；受保护字段不会进入模型上下文。
6. Tool 参数不能指定 TenantId、UserId、OwnerUserId；工具目录不暴露写入接口。

本轮为精确版本源码审计与安全边界更新，尚未实现 CRM Agent Tool 代码；没有宣称工具已上线。


## 10. CRM Chat 内部函数调用链核验（v4.8.0 原生支持，无需借用 MCP）

本轮继续读取目标 ChatAssistant、AI Skill 注册和上游 Framework v4.8.0 AI 会话源码，确认了内部工具接入的真实路径。

### 10.1 已确认的原生工具调用能力

上游 v4.8.0：

- `IXiHanAiService.ChatStreamAsync()` 接受 `XiHanChatOptions`。
- `XiHanChatOptions.ChatOptions` 是原生 `Microsoft.Extensions.AI.ChatOptions`，其中可携带 `Tools`。
- `XiHanAiService.ChatStreamAsync()` 将 `options.ChatOptions` 原样传给 `IChatClient.GetStreamingResponseAsync()`。
- `OpenAiCompatibleChatClientFactory.Create()` 在构建 IChatClient 时无条件调用 `builder.UseFunctionInvocation(_loggerFactory)`，工具调用中间件已经位于客户端管道中。
- 因此，应用服务可以在每个已认证 Chat 请求中创建请求级 `AIFunction`，通过 `XiHanChatOptions.ChatOptions.Tools` 传入模型；函数委托闭包持有该请求 Scope 内的服务。工具执行由已配置的 Function Invocation 中间件处理。此路径不要求把 CRM 查询服务注入 Singleton `IAiSkill`，也不要求把 CRM Tool 暴露到 MCP。

### 10.2 目标 ChatAssistant 当前并未启用工具

目标文件 `backend/src/modules/XiHan.BasicApp.AI/Application/AppServices/ChatAssistantAppService.cs`：

- `ReplyAsync()` 调用 `_aiService.ChatStreamAsync(messages, new XiHanChatOptions { Provider = assistant.ProviderCode }, cancellationToken)`。
- 当前 `XiHanChatOptions` 没有设置 `ChatOptions`，因此没有向本次模型请求提供工具列表。
- 当前 ReplyAsync 只消费 `update.Text`，未看到工具执行结果的专门 UI / 审计流程。
- `KnowledgeRetrieveSkill` 虽在 `AddAISkills()` 中注册，但注册表→MCP 投影与 ChatAssistant 的 `ChatOptions.Tools` 是两条不同通道；不能认定知识检索 Skill 或 CRM Skill 已自动进入聊天工具列表。

### 10.3 第一批 CRM Chat Tool 的安全实现方案

采用“请求级 AIFunction 工厂”，不复用 Singleton Skill：

1. `ReplyAsync()` 已通过 `[Authorize]`、`ChatPermissionCodes.Send`、会话成员检查取得已认证的当前用户和当前租户上下文。Tool 仅在该请求中创建，不提供外部可控的身份参数。
2. Tool 委托只捕获当前请求作用域内的 CRM Query AppService / 必要字段安全服务；不捕获 Repository、DbContext 或任何跨请求状态。
3. 通过 `ICrmCustomerQueryService` / `ICrmLeadQueryService` 查询，继续使用现有 `CrmPermissionCodes.Read`、Owner Data Scope 和 `GuardQueryAsync`。不直接查询 Repository。
4. QueryService 当前详情 DTO 的直接返回路径没有显式 `MaskAsync`；HTTP `FieldSecurityResponseFilter` 不会自动作用于内部函数结果。因此 Tool 必须在返回 AIFunction 结果前显式执行 `IFieldSecurityService.MaskAsync`，并仅对本次新建 DTO 对象脱敏。
5. Tool schema 只允许业务搜索字段、分页大小和受限过滤条件；不允许 `TenantId`、`UserId`、`OwnerUserId`、权限代码或任意字段表达式由模型指定。Owner 范围始终由服务端数据范围服务决定。
6. 首批只做客户与线索只读搜索；详情读取另行确认 ID 不存在与无权访问时的统一响应，防止存在性泄露。自动获客 Campaign/Run/Candidate 工具待相同权限与脱敏路径核验后再加入。
7. Tool 调用记录最少保留工具名、请求用户/租户（从可信上下文取）、参数摘要、结果数量、耗时、成功/失败；不得记录完整敏感客户字段或提示词密钥。

### 10.4 实施前需加的回归测试

- ReplyAsync 只在当前已认证请求内创建 Tool 列表；未授权用户或非会话成员不能触发 CRM Tool。
- `ChatOptions.Tools` 仅包含明确允许的只读函数；模型伪造 `tenantId` / `ownerUserId` 参数应在 schema 或服务端校验阶段拒绝。
- Tool 调用确实经过 `UseFunctionInvocation`，并且函数返回后模型可以继续生成最终答复。
- 两个不同租户/用户并发调用时，Tool 闭包不共享 Scope、CurrentUser、CurrentTenant、DbContext 或脱敏 DTO。
- CRM QueryService 的权限和 Owner Data Scope 被执行；Tool 结果显式 Mask 后才返回模型。
- MCP API Key 未绑定 CRM 主体时仍不得访问 CRM 工具；CRM Chat 内部 Tool 与 MCP Skill 注册分离。
- 工具调用失败、模型取消或模型不支持工具时，Chat 回复流程正确结束，不把异常栈或敏感数据发给用户。

本轮确认了 v4.8.0 原生 Function Invocation 的可行实现路线并更新本文件；尚未修改 ChatAssistant 生产代码，也未宣称 CRM Chat Tool 已经接通。实施时应先落地一个客户只读搜索工具及集成测试，再扩展线索和 Prospecting 查询。
