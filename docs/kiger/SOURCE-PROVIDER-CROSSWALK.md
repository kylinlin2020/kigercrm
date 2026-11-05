# Haituo CRM → KigerCRM 自动获客来源逐项对照表

**基线：** 2026-11-05  
**源仓库：** `hongshuoguo2-cloud/haituo-crm`（main）  
**目标仓库：** `kylinlin2020/kigercrm`（`kiger-dev`）

## 1. 源项目真实 Provider 数量口径

源项目 `backend/src/lead-providers.ts` 的 `LEAD_PROVIDERS` 聚合 34 个来源（含公开公司、采购和人工辅助来源）；`backend/src/trade-providers.ts` 另有 3 个贸易统计 Provider。合计 **37 个真实 Provider 注册项**。Provider Catalog 另有一个 `ai_search` 虚拟能力项，它不是普通外部 Provider，不能混入 37 个接口来源统计。

本表按源代码注册清单逐个核对，不把产品页面、策略、网站发现、Super Search 或 Deep Mining 当作数据源数量。

状态定义：
- **已实现待验收**：目标分支已有真实 API 适配器源码，仍须执行构建、测试、真实 API 和 Windows/PostgreSQL 验收。
- **暂缓（许可/架构）**：不应在许可或数据保存设计未解决前接入。
- **未迁移**：尚无目标端等价 Provider。
- **人工辅助待迁移**：源项目只是人工/文件/受控网站入口，不应伪装成自动 API Provider。
- **接口已核实待适配器**：已确认接口和字段，但目标 Provider 尚未实现。

## 2. 34 个获客 Provider 逐项对照

| # | 源 Provider ID | 源项目名称 | 目标端状态 / 对应项 |
|---:|---|---|---|
| 1 | `serper` | Serper (Google) | 已实现待验收：`CrmSerperSearchProspectingProvider` |
| 2 | `brave` | Brave Search | 已实现待验收：`CrmBraveSearchProspectingProvider` |
| 3 | `serpapi` | SerpApi (Google) | 已实现待验收：`CrmSerpApiProspectingProvider` |
| 4 | `google_places` | Google Places | 暂缓：CRM 持久化与 Places 内容缓存/存储政策冲突；先解决 Place-ID-only 方案或取得书面许可 |
| 5 | `gleif` | GLEIF 法人库 | 已实现待验收：GLEIF 身份解析/补充 Provider |
| 6 | `wikidata` | Wikidata 公开实体 | 已实现待验收：`CrmWikidataProspectingProvider` |
| 7 | `eu_ted` | EU TED | 已实现待验收：EU TED 公共采购 Provider |
| 8 | `world_bank_procurement` | World Bank Procurement | 已实现待验收：World Bank Procurement Provider |
| 9 | `uk_contracts_finder` | UK Contracts Finder | 已实现待验收：UK Contracts Finder Provider |
| 10 | `companies_house` | Companies House (UK) | 已实现待验收：`CrmCompaniesHouseProspectingProvider` |
| 11 | `nl_kvk` | Netherlands KVK | 已实现待验收：`CrmKvkProspectingProvider` |
| 12 | `opencorporates` | OpenCorporates | 暂缓：多租户/第三方访问与营销用途许可门槛，先取得明确商业授权 |
| 13 | `sec_edgar` | SEC EDGAR | 本轮新增已实现待验收：`CrmSecEdgarProspectingProvider`；仅美国市场、SEC Fair Access User-Agent、公司身份线索 |
| 14 | `fr_company_search` | 法国企业名录 | 已实现待验收：`CrmFranceCompanySearchProspectingProvider` |
| 15 | `ror` | ROR 全球机构库 | 已实现待验收：`CrmRorProspectingProvider`；研究机构而非通用商业企业库 |
| 16 | `nppes` | 美国 NPPES 医疗机构 | 已实现待验收：Type 2 机构记录；医疗 ICP 专用 |
| 17 | `openfda_510k` | 美国 FDA 510(k) | 已实现待验收：医疗器械监管证据，不等同采购意向 |
| 18 | `usaspending_awards` | USAspending 联邦采购 | 已实现待验收：USAspending Provider |
| 19 | `uk_find_a_tender` | 英国 Find a Tender | 已实现待验收：UK Find a Tender Provider |
| 20 | `sam_opportunities` | 美国 SAM.gov 商机 | 已实现待验收：SAM.gov Opportunities Provider |
| 21 | `brazil_pncp` | 巴西 PNCP 采购 | 已实现待验收：Brazil PNCP Provider |
| 22 | `mexico_denue` | 墨西哥 DENUE 企业名录 | 未迁移：源项目有 INEGI Token API 适配器；需单独迁移并审查 Token URL 脱敏 |
| 23 | `fmcsa_qcmobile` | 美国 FMCSA 承运商 | 未迁移：源项目有官方 QCMobile WebKey 适配器；属于物流承运商行业专用，不应作为普通发电机/水泵主来源 |
| 24 | `singapore_gebiz` | 新加坡 GeBIZ 中标数据 | 已实现待验收：当前使用 data.gov.sg 已授标数据集，明确不是开放招标 |
| 25 | `canadabuys_open_data` | CanadaBuys 开放采购数据 | 人工辅助待迁移：源项目使用官方批量下载文件，不是实时 API Provider |
| 26 | `eprel` | 欧盟 EPREL 产品库 | 人工辅助待迁移：受控产品注册网站入口；用于产品/制造商核验，不等同买家名单 |
| 27 | `eu_ecolabel` | 欧盟 Ecolabel 产品目录 | 人工辅助待迁移：认证/产品证据入口，不等同采购意向 |
| 28 | `china_customs_statistics` | 中国海关统计 | 人工辅助待迁移：官方聚合贸易统计入口，不得伪装成企业级进口商数据 |
| 29 | `importyeti` | ImportYeti 进口商检索 | 人工辅助待迁移：源项目明确 manual_assisted；先遵守站点条款，不做未授权爬取 |
| 30 | `koneps` | 韩国 KONEPS 采购平台 | 人工辅助待迁移：源项目为官方门户人工检索入口；API/自动化许可另行核验 |
| 31 | `sbir_awards` | 美国 SBIR/STTR 获奖企业 | 人工辅助待迁移：源项目当前为人工检索入口，获奖记录不是采购需求 |
| 32 | `apollo` | Apollo.io | 暂缓：内部使用/第三方访问许可需先确认 |
| 33 | `pdl` | People Data Labs | 暂缓：数据再提供/终端用户访问许可需先确认 |
| 34 | `hunter` | Hunter.io | 已实现待验收：`CrmHunterDiscoverProspectingProvider`；Discover 阶段只取企业域名/名称和邮箱数量统计，不取联系人邮箱 |

## 3. 3 个贸易统计 Provider 逐项对照

| # | 源 Provider ID | 源项目名称 | 目标端状态 / 对应项 |
|---:|---|---|---|
| 1 | `un_comtrade` | UN Comtrade | 已实现待验收：`CrmUnComtradeProspectingProvider`；聚合商品贸易信号，不是具体进口商 |
| 2 | `us_census_trade` | US Census International Trade | 已实现待验收：`CrmUsCensusTradeProspectingProvider`；美国官方贸易统计 |
| 3 | `eurostat_comext` | Eurostat Comext | 接口已核实待适配器：同步查询边界已确认；通用 Provider contract 尚缺异步 job 续取能力，优先实现严格小查询或扩展异步执行契约 |

## 3.1 Eurostat Comext 适配器实施边界（官方接口复核）

- Comext/DS 前缀数据集必须使用独立 base URL：`https://ec.europa.eu/eurostat/api/comext/dissemination`，不能误用普通 Eurostat dissemination endpoint。
- 当前核对的数据集为 `DS-045409`。SDMX 2.1 数据路径形态为 `/sdmx/2.1/data/DS-045409/{freq}.{reporter}.{partner}.{product}.{flow}.{time_period}.{indicators}`；维度顺序必须按数据集结构，不得把普通参数顺序套用到 Comext。
- 官方数据约束示例显示该数据集包含 2 个 freq、33 个 reporter、282 个 partner、40,321 个 product、2 个 flow、468 个 time_period 和 3 个 indicators。无过滤全量查询不可接受。
- 官方估算规则：低于 500,000 cells 通常同步返回；500,000 至 5,000,000 cells 可能异步返回；超过 5,000,000 cells 返回 HTTP 413，要求进一步过滤。即使估算低于同步阈值，公平使用策略也可能将请求转为异步。
- 异步工作流包括提交后取得 extraction ID、查询 `/1.0/async/status/{id}`、状态为 AVAILABLE 后读取 `/1.0/async/data/{id}`。当前 `ICrmProspectingProvider.SearchAsync` 是单次同步结果契约，不能把 SUBMITTED/PROCESSING 当作空结果或成功覆盖。
- 实施顺序：先实现通用异步 Provider Job 持久化（tenant + run + provider + extraction ID + status + next poll + expiry），再实现 Comext；在此之前仅允许有严格维度过滤且可证明小于同步阈值的请求，并将 HTTP 413/异步提交识别为 coverage incomplete。
- 商品编码必须来自 Comext 官方 codelist/约束元数据并经版本化映射；不得仅凭用户输入的自然语言“发电机/水泵”猜测 HS 编码。Reporter、Partner、Flow、Time 和 Indicator 均须显式受限。
- 官方依据：
  - [Comext 专用 API base 与数据集说明](https://ec.europa.eu/eurostat/en/web/user-guides/data-browser/api-data-access/api-getting-started/comext-database)
  - [Comext DS 前缀数据集规则](https://ec.europa.eu/eurostat/en/web/user-guides/data-browser/api-data-access/api-detailed-guidelines/prefixed-datasets)
  - [异步提取阈值与续取流程](https://ec.europa.eu/eurostat/web/user-guides/data-browser/api-data-access/api-detailed-guidelines/asynchronous-api)

## 4. 源项目额外获客能力（不计入 37 个 Provider）

| 能力 | 源项目代码/测试证据 | 目标端核对状态 |
|---|---|---|
| AI Search | Provider Catalog 的 `ai_search` 能力项；Super Search 规划器支持 AI discovery | 待核验：需接入目标端现有 AI 框架，标记 assisted evidence、模型和提示词版本 |
| 网站发现 / AI 网站发现 | 源项目 Deep Mining / Website Opportunity 管道 | 未完成等价迁移验收：必须 SSRF 防护、固定安全出站边界、证据与人工复核 |
| Super Search | `prospect-super-search.ts`、`prospect-search-planner.ts`、`prospect-super-search-test.ts` | 目标端待做完整功能对照与端到端验收；重点检查轮次规划、动态查询、收敛判断、暂停/恢复、事件、幂等 |
| Deep Mining | `prospect-deep-mining-test.ts` | 目标端待做完整功能对照与端到端验收；重点检查深度等级、子任务、候选同步、证据评分、租户隔离 |
| Provider Catalog / 统一运行时 | `provider-catalog.ts`、`provider-runtime.ts`、请求账本/缓存测试 | 目标端待核验目录元数据、来源权限、成本/配额、缓存隔离、请求账本和健康检查完整性 |
| Provider Trade Runtime | `provider-trade-runtime.ts`、`market-trade-observations.ts` | 目标端待核验贸易观察值、来源修订、HS 分类版本、单位/币种转换与租户隔离 |

## 4.1 Provider Catalog / Runtime 源能力验收矩阵（新增审计）

本节依据源项目 `provider-catalog.ts` 与 `provider-runtime.ts` 的实际实现拆分，不把“Provider 类已迁移”误判为运行时能力已迁移。目标端逐项验收时必须记录代码位置、测试和运行证据；未完成项保持待验收。

| 源项目运行时能力 | 源项目实现语义 | 目标端验收要求 |
|---|---|---|
| 统一 Provider Catalog | 统一列出 lead/trade Provider 与虚拟 `ai_search`；包含分类、来源等级、接入方式、能力、允许字段、逐字段权威等级、Key/成本/许可策略、缓存范围、限速策略、保留策略、状态与版本 | 对照目标端 Provider 注册、配置面板和 API 响应；不能只靠 DI 注册表推导来源能力。每个字段需有 authority/evidence 级别，避免把网页发现、AI 推断、官方登记和采购公告混为一谈 |
| 操作权限与契约校验 | 执行前核对 catalog code 与 adapter ID、contract version、access mode、active 状态、操作能力（search/enrich/trade）及双方 capabilities 交集 | 目标端必须在执行入口做服务端校验；前端隐藏按钮或 Provider 默认关闭不能替代服务端策略校验 |
| 租户凭证隔离 | 执行上下文要求 team/owner/run/shard/request/purpose；连接必须为 personal scope，且 provider/owner/team 与当前上下文完全一致；凭证解密失败按连接无效处理 | 目标端继续使用租户加密配置；补充跨租户、跨 owner、错误 Provider、禁用凭证和解密失败测试。不得把 Key 放入日志、请求指纹或普通错误文本 |
| 统一请求账本与脱敏日志 | `runLogged` 为请求生成 fingerprint，记录 team/owner/provider/connection/run/shard/endpoint，并统一规范化异常、HTTP 状态、耗时、响应大小和错误码 | 对照目标端 Run/Provider 日志，确认每次外部请求可追踪且租户隔离；Key、Authorization、URL query/path 中的秘密必须脱敏；记录失败也不能泄漏原始响应秘密 |
| 请求指纹与缓存隔离 | 指纹绑定 provider ID、adapter version、catalog policy version、endpoint 和规范化请求；trade cache key 另绑定 license scope | 目标端缓存必须按租户/许可范围/适配器版本隔离；不得跨租户共享有凭证或有许可限制的数据；变更查询参数或策略版本必须产生不同指纹 |
| 并发请求合并 | Trade runtime 对相同 provider/version/request fingerprint/license scope 使用 single-flight，避免并发重复调用；失败会规范化并向 follower 传播 | 目标端需明确是否实现同请求合并；如未实现，必须有并发配额保护和重复请求成本测试，不得把“数据库去重”当作请求合并 |
| 连接级速率预算 | Trade runtime 支持 max concurrent、minimum interval、requests/minute，并返回带 Retry-After 时间的可重试限流错误 | 目标端对所有 Provider 的并发/间隔/每分钟预算应可配置且按租户连接隔离；Provider 原生 403/429/503 语义须按来源文档分类，不能统一重试 |
| 缓存命中与使用量语义 | Trade cache 命中返回 cache status，并将 requestCount/quotaUsed 归零；并发复用也标为 cache；缓存写失败降级为 partial_success 并给 warning | 目标端必须区分 live/cache、实际调用次数、配额消耗和缓存失败；不能把缓存结果记成新一次 API 成功调用 |
| 独立操作面 | Search、Enrich、Trade、Health、Preflight 分别执行；域名补全有输入校验；trade 单独分页并校验 cursor | 目标端需明确是否具备独立的健康检查、预检、补全和贸易查询入口；不能把所有能力压缩为一次 SearchAsync 后宣称功能等价 |
| 分页与覆盖完整性 | Search/Trade 使用 cursor 分页；cursor 长度限制；页面经字段 allowlist、authority 和 retention policy 规范化 | 目标端每个 Provider 必须证明分页终止、结果上限和 coverage incomplete 语义；达到页数/记录上限、被限流或异步提交时不能报告完整覆盖 |

### 目标端 Provider 契约静态复核（2026-11-05）

已读取目标端 Provider contracts、Run HostedService、HTTP handler factory、Prospecting AppService 与 CRM DI 注册代码，确认以下边界：

- `ICrmProspectingProvider` 目前只有 `ProviderCode` 和 `SearchAsync`；没有独立 Health、Preflight、Enrich、Trade Query 或异步 continuation 方法。
- `CrmProspectingProviderResult.IsComplete` 默认 false。适配器必须显式说明是否耗尽结果，不能把分页截断视为完整覆盖。
- HostedService 已有 Redis 延迟队列、租户上下文、Run lease/heartbeat/fencing 和 Provider Request Ledger。它们是 Run 级执行设施，不等同于 Comext extraction Job 的持久化轮询、过期和恢复。
- HTTP handler 已固定允许 host、禁用自动重定向、校验 DNS 公网地址并连接已验证 IP。DENUE Token 位于 URL path，仍需单独证明完整 URI 不会进入应用、代理或诊断日志。
- Provider DI 注册存在，但仅有 SearchAsync + DI 不代表源项目 Runtime parity。

新增阻断项：在实现 Comext 前，先设计独立租户级 Provider Job 实体/仓储/状态机，包含 Run/Provider 关联、Extraction ID、NextPollAt、ExpiresAt、幂等轮询、取消与恢复；并以向后兼容方式扩展 Health/Preflight/Enrich/Trade 操作面。不得把异步 SUBMITTED/PROCESSING 当作空成功结果。以上是静态代码复核，尚未执行构建或运行测试。

补充核验 Run Worker 的覆盖率提交顺序与截断边界：

- Worker 仅在 `IsComplete == true`、`FailureCount == 0` 且记录数/单条记录大小/总 JSON 大小均未超过上限时，才把 Provider Request Attempt 放入待提交 coverage 列表。
- 对不完整分页、记录级失败、Run 记录数上限或无效/超大记录，先以 Failed 状态结束该次请求账本，不推进 incremental coverage；返回记录仍可在保留上限内进入后续候选处理。
- coverage checkpoint 只有在候选/证据持久化完成后才提交，避免 Worker 在数据尚未落库时先推进 checkpoint 导致漏数。
- Worker 对有 checkpoint 的来源回退 48 小时重叠窗口，并通过 `CoverageSinceUtc` 传递给支持增量扫描的 Provider。

以上逻辑是目标端当前已存在的正向实现，可作为后续 Provider adapter 的统一验收标准。注意这仍不解决源端多页 Provider 的 continuation orchestration：当前 Provider contract 每次只返回一个 `SearchAsync` 结果，分页 Provider 必须在 adapter 内完整续页，或等待未来引入可持久化 continuation/Job contract。


### 源端运行时符号级证据（2026-11-05 复核）

本轮重新读取源仓库以下真实文件，而非仅依据文件名推断能力：

- `backend/src/provider-runtime.ts`：导出 `assertProviderOperationPolicy`、`executeProviderPreflight`、`executeProviderSearch`、`executeProviderHealth`、`executeProviderEnrich`、`executeProviderTradeQuery`；实现 Provider 操作策略校验、Search/Trade cursor 长度限制、请求 fingerprint、统一请求日志、Trade cache、连接级预算与并发请求复用。
- `backend/src/lead-providers.ts`：Provider contract 明确支持 `health()`；部分来源提供 `enrich()`，如 Hunter；不能把仅有 `search()` 的适配器视为具备补全能力。
- `backend/src/provider-catalog.ts`：`createDefaultProviderCatalog()` 生成目录，并把来源能力、分类和缓存/速率策略作为运行时元数据；目录并非单纯的 DI 列表。
- `backend/src/prospect-search-planner.ts`：导出 `planProspectSearchRound`、`enhanceProspectSearchRoundPlan`、`validateProspectSearchQueryPlan`、查询计划 fingerprint 与 provider-resolved query fingerprint；规划结果包含 coverage gaps 和 query cells。
- `backend/src/prospect-super-search.ts`：维护多轮 mission/round 状态、轮次统计、成本完整性、收敛判断、深挖任务和轮次完成事件；包含 `prospectSuperSearchConvergenceReason` 等状态推进逻辑。
- `backend/src/market-trade-observations.ts`：导出贸易观察记录 identity、upsert、可见性过滤和应用逻辑，说明 Trade Provider 之外还存在持久化与租户可见性业务层。

据此，目标端逐项验收不能只检查 Provider 类和 DI 注册。至少要分别证明：服务端 operation policy、health/preflight/enrich 独立入口、请求 fingerprint/ledger、Trade cache 与 single-flight、连接预算、Super Search 查询计划版本化/恢复/收敛、Deep Mining 状态同步，以及 Trade observation 的身份去重和租户可见性。未找到目标端代码或测试证据时保持“待核验”，不因源端已确认而推断目标端已实现。

### SSRF / DNS 特殊用途地址回归审计（2026-11-05）

- 复核目标端 `CrmProspectingHttpHandlerFactory.IsPublicAddress` 与 `CrmProspectingEndpointGuardTests` 时发现：测试已明确要求拒绝 `192.175.48.0/24`（AS112 特殊用途地址），但生产 IPv4 deny-list 漏掉该网段，形成“测试期望与实际实现不一致”的防护缺口。
- 已在生产过滤器中补入 `192.175.48.0/24` 拒绝规则，提交 `e6d7eb5cb4ba53f3f18123e9829836fef9bf3271`。现有 `CrmProspectingEndpointGuardTests` 已包含 `192.175.48.1 => false` 的回归用例，无需重复造测试。
- 这项修复尚未执行 .NET 测试；后续验收仍须实际运行 `CrmProspectingSafeHttpHandlerTests.IsPublicAddress_ShouldApplyConservativePublicUnicastPolicy`，并继续核对 DNS 多地址混合应答、IPv4-mapped IPv6、重定向关闭和连接固定行为。

### 当前迁移控制结论

- 源项目除了 37 个 Provider 注册项，还存在统一目录、执行策略、租户凭证校验、请求账本、指纹/缓存、并发预算、single-flight、健康检查、预检、联系人补全和贸易查询等独立运行时能力。
- 当前目标端获客 Provider 的 `ICrmProspectingProvider.SearchAsync` 单次同步契约不足以表达源项目完整的 Search/Enrich/Trade/Health/Preflight 生命周期，也不足以表达 Comext 异步任务续取。不能以 23 个适配器源码数量代表整个自动获客能力完整迁移。
- 本轮只补充源能力基线和验收矩阵，尚未声称上述目标端能力全部实现；后续应按矩阵逐项定位目标代码、补充测试和缺口实现，再更新迁移状态。

## 5. 本轮 SEC EDGAR 适配器范围

- 固定调用 `https://www.sec.gov/files/company_tickers.json`，只访问 SEC 官方主机；进程内统一限速为每 150 ms 最多一次（约 6.7 req/s），低于 SEC 当前 10 req/s 上限。
- 需要用户填写 SEC Fair Access User-Agent（系统名称 + 联系邮箱），不是 API Key；保存到租户加密配置 `CRM.Prospecting.Provider.sec_edgar.UserAgent`。
- 仅美国目标市场运行；公司名称/股票代码与活动 ICP/产品/行业关键词做本地匹配；最多输出 100 条；SEC 官方说明 ticker/CIK 关联文件不保证完整准确或覆盖全部申报主体，因此不得当作全美企业名录。
- 记录 CIK、Ticker、官方 SEC browse URL、检索时间、原始记录 SHA-256；证据类型 `us_sec_company_directory`。
- 不抓取联系人、不把 SEC 登记/上市记录解释为采购意向、不自动写入正式客户。
- `EnableSecEdgarProvider=false` 默认关闭。
- 当前代码与契约测试已提交；尚未运行 .NET 构建/测试、SEC 真实 API 冒烟或 Windows/PostgreSQL 联调。当前限速器是进程内共享；若以后允许多个 API/Worker 进程并行，必须先把 SEC 限速升级为跨进程共享协调器。

## 6. 迁移验收原则

- **37/37 是源项目 Provider 注册项基数，不是目标迁移完成数量。**
- 人工辅助入口必须迁移为明确的人工工作流/受控导入，不得通过自动爬取冒充等价迁移。
- 暂缓来源在许可门槛解除前不应开发或启用。
- 每个真实 Provider 的“代码存在”与“来源验收完成”分开计数。
- 最终验收必须包括目标 Windows 原生环境拉取、构建、单元/集成测试、PostgreSQL 联调、真实 API smoke test、租户隔离与人工审批检查。


## 2026-11-05 — Async extraction capability contract

The target branch now has an optional `ICrmProspectingAsyncExtractionProvider` capability alongside the unchanged synchronous `ICrmProspectingProvider`. It defines submit, poll, bounded result-page fetch, and cancellation operations. Remote provider status is distinct from the durable local extraction lifecycle, which includes FetchingResults and Completed.

A pure `CrmProspectingExtractionStateMachine` rejects terminal-state regression and only permits result completion from FetchingResults when the final page is complete and has zero record failures. Contract/state tests were added but have not been executed.

This is contract-level migration only. There is no extraction-job persistence, scheduler/recovery worker, result-page persistence, PostgreSQL migration, or Comext adapter yet. Do not count this capability as full source runtime parity.


## 2026-11-05 — Durable extraction job persistence layer

The target branch now includes a strict-tenant extraction-job entity and PostgreSQL repository. The repository supports idempotent tenant/run/provider/fingerprint creation, tenant-scoped reads, due-job claiming with `FOR UPDATE SKIP LOCKED`, database-clock leases, per-job lease tokens, fencing increments, expiry recovery, guarded state transitions, and cursor advancement guarded by the expected cursor and active PostgreSQL transaction.

Migration `5.9.33` creates the durable table and indexes. CRM entity metadata and DI registrations are included. Contract tests cover strict tenant scope, repository operations, lifecycle/provider-status separation, and required persistence fields.

This is persistence groundwork only. No worker currently consumes the repository; no provider submission/poll/fetch orchestration, candidate/evidence page persistence, transaction-level cursor integration test, coverage advancement, or Comext adapter is complete. No build/test/DB execution is claimed.


Persistence hardening follow-up: the generic provider metadata JSON is deliberately not accepted by the repository creation API or persisted yet; failure codes are allowlist-normalized and failure summaries are fixed text only. Expiry recovery uses PostgreSQL database time, and cursor advancement requires an active PostgreSQL transaction. This reduces secret leakage and prevents a cursor from advancing independently of page persistence.


Async extraction boundary follow-up: added bounded page validation (record/cursor/serialized-byte caps, duplicate identity rejection, HTTP(S) source URL validation, SHA-256 validation, and strict final-page markers) plus a remote-to-local status mapper. Unknown numeric remote statuses map to `NeedsReview`; provider retry hints are clamped to a five-second to thirty-minute window. These are tested by new unit test source files, not yet executed. PostgreSQL now generates per-claim lease tokens with `gen_random_uuid()`.


Lease fencing follow-up: conditional lifecycle transitions and cursor compare-and-swap now also require the lease expiry to remain later than PostgreSQL `clock_timestamp()`. Possession of an old token after expiry is insufficient to write, even before a replacement worker claims the job.
