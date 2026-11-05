# 自动获客数据源全量迁移台账（P0）

**基线日期：** 2026-11-05  
**源项目：** `hongshuoguo2-cloud/haituo-crm`（main）  
**目标项目：** `kylinlin2020/kigercrm`（`kiger-dev`）  
**用途：** 逐项追踪源项目的数据源能力、目标实现、启用条件和验收证据。此台账是迁移控制文件，不代表所有来源已接通。

## 1. P0 定义与完成门槛

自动获客数据源完整迁移是 P0，不以“已有自动获客页面/Worker”或“已有若干 Provider 类”代替完成验收。

每个来源必须分别记录并验证：

1. 适配器：真实 API/公开数据接口可调用；不以 Fake、静态目录项或人工链接冒充。
2. 配置：地区、认证方式、密钥要求、启用开关、费用/配额说明明确；付费来源默认关闭。
3. 采集：关键词/地区/日期范围、分页/游标、结果上限、取消和超时行为正确。
4. 证据：稳定来源记录 ID、原始来源 URL、检索时间、发布日期（如有）、证据类型和字段来源可追溯。
5. 可靠性：限流、重试预算、失败分类、响应大小限制和网络目的地安全得到测试。
6. 数据质量：标准化、跨 Run 去重、身份冲突保留、无证据字段不臆造。
7. 合规：条款、许可、数据保留、个人数据处理及调用成本经过审查。
8. 验收：单元/契约测试、DI 注册测试、真实来源冒烟测试及 Windows + PostgreSQL 集成验收有记录。

只有来源级验收全部通过，才能把该来源标记为“完成”。“代码已存在”仅代表源码实现，不代表运行验收。

## 2. 来源清单与目标状态

状态说明：
- **已实现待验收**：目标分支有真实适配器源码，但尚未完成完整运行/真实来源验收。
- **未迁移**：尚未发现目标端等价真实适配器。
- **需拆分确认**：源项目存在辅助来源或行业入口，必须确认其究竟是可自动调用 API、公开数据集还是人工辅助入口，不能直接计为自动 Provider。

### A. 搜索发现与地图企业来源

| 源项目来源 | 类别 / 认证 | 目标状态 | 后续工作 |
|---|---|---|---|
| Serper（Google） | 搜索 API；需租户 Key；按 credits 计费 | 已实现待验收 | 已新增 Google organic search Provider；租户加密 Key、X-API-KEY Header、最多 20 条/次；满页标记覆盖不完整 |
| Brave Search | 搜索 API；需 Token/付费计划 | 已实现待验收 | 已新增 Web Search API Provider；租户加密 Token、国家定向、最多 2 页×20 条、尊重 more_results_available；结果仅作为网页发现证据 |
| SerpApi（Google） | 搜索 API；需租户 Key；免费/付费额度 | 已实现待验收 | 已新增 Google Search Provider；租户加密 Key、受限两页分页；API 要求 key 在 query string，诊断必须脱敏 |
| Google Places | Places API (New) Text Search；需启用 Billing + API Key/OAuth；按 FieldMask SKU 计费 | 暂缓接入（持久化许可/架构门槛） | 已核验新版 POST、FieldMask、20 条/页、跨页最多 60 条；但 Google Places 内容不得超出政策例外范围预取/缓存/持久化，只有 Place ID 明确豁免。当前获客 Provider contract 会把结果持久化为 CRM 线索证据，直接保存名称/地址/网站/电话存在条款风险；不能简单做适配器后写入 CRM。须先设计仅保存 Place ID、按需实时取数并在合规 UI 中展示（含 Google attribution/Terms/Privacy），或取得 Google 明确书面许可；在此之前不开发、不调用。

### B. 官方企业身份、注册和行业名录

| 源项目来源 | 类别 / 覆盖 | 目标状态 | 后续工作 |
|---|---|---|---|
| GLEIF | 全球 LEI 法人身份 | 已实现待验收 | 核验精确 LEI/名称查询、匹配置信度和冲突处理 |
| Wikidata | 全球知识图谱实体发现；公开 API、无需 Key | 已实现待验收 | 新增低频 `wbsearchentities` 关键词发现 Provider；最多 3 个关键词×10 条；仅保留组织描述线索；上限命中标记 incomplete；不等同企业注册或采购意向 |
| OpenCorporates | 多国企业登记聚合；需 API Key，商业使用许可有严格限制 | 暂缓接入（许可门槛） | 2026-11-05 已核实 Self-serve API 条款：默认 internal-use 仅限本企业员工/承包商，不得向第三方披露 API 数据，也不得让第三方可用的产品/服务依赖该 API；restricted_for_marketing 数据不得用于营销/广告。商业 CRM / 多租户 SaaS 场景必须先取得明确允许该部署形态及获客用途的书面许可/非 share-alike 商业许可，并确认署名、数据留存与 jurisdiction restrictions；未确认前不开发、不调用。
| Companies House | 英国官方企业登记；需租户 API Key（HTTP Basic 用户名） | 已实现待验收 | 已新增真实公司搜索 Provider；仅英国目标市场调用，最多 2 页，超限标记 coverage incomplete；公司登记不等同采购意向 |\n| SEC EDGAR | 美国 SEC 官方公司/股票代码目录；免费，但必须提供 Fair Access User-Agent 联系邮箱 | 已实现待验收 | 新增 `sec_edgar` Provider：固定读取 `www.sec.gov/files/company_tickers.json`；仅美国目标市场；租户加密配置保存系统名+联系邮箱（不是 API Key）；最多 100 条；进程内 150 ms 间隔限速（约 6.7 req/s，低于 SEC 10 req/s）；只作为 CIK/Ticker 公司身份线索，不含联系人或采购意向 |
| Netherlands KVK | 荷兰官方企业登记；订阅/Key | 已实现待验收 | 已新增真实 Zoeken API Provider；租户加密 Key、仅 NL 市场调用、最多 3 个关键词×每词 2 页；搜索结果只作为企业登记证据 |
| France company search（fr_company_search） | 法国政府开放企业目录；免 Key | 已实现待验收 | 已新增官方 Recherche d’Entreprises API Provider；限法国市场、SIREN/SIRET 企业身份字段、最多 3 词×2 页 |
| ROR | 全球研究机构标识；公开 REST API、无需 Key | 已实现待验收 | 新增真实 v2 organizations 查询 Provider；最多 3 个关键词×每词 20 条；记录研究机构类型/国家/网站；上限命中标记 incomplete；不作为普通公司库 |
| US NPPES | CMS NPPES/NPI Registry；免费公开 API/全量 DDS 文件 | 已实现待验收 | 新增 `nppes` Provider：仅美国目标市场、医疗行业活动、强制 `enumeration_type=NPI-2`；最多 2 个关键词×2 页×200 条，满页上限标记 incomplete；不采集 NPI-1 个人记录、不把 NPI 当作执照/资质背书。CMS 对每小时查询实施限流，未公开具体额度；批量场景必须评估官方 DDS 文件，不以 API 高频扫库。
| FDA 510(k) | openFDA Device 510(k) Clearances；公开 API，无 Key 可用（常规使用建议申请 Key） | 已实现待验收 | 已新增 `open_fda_510k` Provider；仅美国目标市场且活动明确涉及 medical device/medical equipment 时运行；租户加密 Key 可选；每关键词最多 3 页×100 条，超限标记 incomplete；提取申请人、器械名称、K 编号、决定日期和产品代码；结果仅为医疗器械监管证据，不等同采购意向或普通企业注册。
| Mexico DENUE | 墨西哥 INEGI 经济单位目录；需 API Token | 暂缓实现（凭证路径泄漏风险待解决） | 源适配器把 Token 放在 URL 路径段；在确认 HttpClient/反向代理/诊断日志全链路不会记录 Token 前，不得直接迁移。先核验官方是否支持 Header 认证；若仅支持路径 Token，则必须采用受控日志脱敏/专用出站处理并增加 Token 不出日志测试，仍保持租户隔离与默认关闭 |
| US FMCSA QCMobile | 美国运输承运商记录 | 需拆分确认 | 明确行业范围、可用 API 与字段含义 |

### C. 公共采购、招标与政府合同来源

| 源项目来源 | 类别 / 覆盖 | 目标状态 | 后续工作 |
|---|---|---|---|
| EU TED | 欧盟官方招标公告 | 已实现待验收 | 完整分页、公告阶段/买方字段、地区与时间覆盖验收 |
| World Bank Procurement | 世界银行官方采购公告；公开接口 | 已实现待验收 | 已新增真实适配器与 opt-in DI、契约测试；当前单页上限 20，若有更多结果明确标记 coverage incomplete；下一步扩展可靠续页/日期覆盖并做真实 API 冒烟 |
| USAspending | 美国联邦合同/支出 | 已实现待验收 | 完整分页、金额/机构字段、真实 API 冒烟 |
| UK Find a Tender | 英国官方招标公告 | 已实现待验收 | 游标续页、公告/买方字段和限流验收 |
| UK Contracts Finder | 英国公共采购公告 | 已实现待验收 | 游标续页、公告阶段、买方与联系字段验证 |
| SAM.gov opportunities | 美国联邦招标机会；需 API Key | 已实现待验收 | 已新增官方 v2 Opportunities 搜索适配器；按最近 30 天窗口、最多 3 页；租户密钥从加密 SaaS 配置读取；映射公开采购联系人字段但不自动外联；需验收真实 Key 权限、额度、分页和字段映射 |
| Brazil PNCP | 巴西国家公共采购门户；公开接口 | 已实现待验收 | 已新增真实适配器、国家匹配、最多 5 页 bounded fetch、证据映射和契约测试；超过页数标记不完整 |
| Singapore GeBIZ | data.gov.sg GeBIZ 已授标数据集 | 已实现待验收 | 已新增真实适配器、国家匹配、最多 5 页 bounded fetch；明确为历史授标记录而非开放招标；契约测试待运行 |
| CanadaBuys | 加拿大联邦采购 | 需拆分确认 | 源项目为 assisted source，确认可自动 API 与人工辅助边界 |
| KONEPS | 韩国公共采购 | 需拆分确认 | 确认开放 API、认证、语言和许可 |
| SBIR/STTR awards | 美国创新研发政府资助/合同数据 | 需拆分确认 | 确认数据集接口、获奖主体与采购意向的区别 |

### D. 国际贸易、进口商与产品合规信号

| 源项目来源 | 类别 / 覆盖 | 目标状态 | 后续工作 |
|---|---|---|---|
| UN Comtrade | 官方国际商品贸易统计 | 已实现待验收 | 已新增公开预览（无 Key，最多 500 行）与订阅 Key 数据 API 两种模式；按目标市场 Reporter 取数，结果标记为聚合贸易信号，不视为具体进口商；需验证配额、分类与月份覆盖 |
| US Census International Trade | 美国官方进出口统计 | 已实现待验收 | 已新增 HS2 出口贸易统计适配器，Key 必填，默认关闭；从当前租户加密配置读取 Key，并按 target market 国家过滤目的地；需验收变量/分类/时间覆盖 |
| Eurostat Comext | 欧盟官方商品贸易统计；免费 SDMX API | API 与维度已核实，待适配器 | 已确认 DS-045409 与 Comext 专用 API base；维度为 freq/reporter/partner/product/flow/time_period/indicators；必须过滤到很小的 reporter/partner/HS 产品/月份集合；大查询触发异步作业，当前 Provider contract 尚无异步 job 状态/结果续取能力 |
| China Customs Statistics | 中国海关统计入口 | 需拆分确认 | 核验接口可用性、许可、数据粒度及是否聚合统计 |
| ImportYeti | 美国提单/进口商发现 | 需拆分确认 | 确认是否有正式 API、许可和自动化访问边界；禁止绕过访问控制 |
| EU EPREL | 欧盟能源标签产品登记 | 需拆分确认 | 作为产品/合规信号源，明确能否可靠反查企业 |
| EU Ecolabel | 欧盟生态标签产品/持证信息 | 需拆分确认 | 作为产品/认证证据，不直接等同买家名单 |

### E. 商业公司与联系人补全来源

| 源项目来源 | 类别 / 认证 | 目标状态 | 后续工作 |
|---|---|---|---|
| Apollo.io | 商业企业/联系人数据；需 Key/付费 | 暂缓接入（API 内部使用许可门槛） | API Terms 当前可见版本（2024-08-13）仅授予 internal business purposes，禁止第三方访问、转授权/分发或未获授权集成；Apollo 2026 ToS 允许订阅期内把 Business Contact Information 纳入自有 CRM/工作流，但仍限内部业务使用且禁止向第三方提供数据/API。只有确认该部署仅供本企业授权员工/承包商内部使用、无外部客户访问/转售/数据共享，并核对当前订单/主服务协议与 API 条款一致后，才可开发并启用；否则须先取得书面许可。默认关闭，Key 必须租户加密保存，严格额度/成本预算。
| People Data Labs（PDL） | 商业企业/人员数据；需 Key/付费 | 暂缓接入（数据再提供/终端用户访问许可门槛） | PDL Subscription Agreement 禁止转售、再许可或使第三方获得 Data；禁止构建可让 End Users 程序化访问实质性 PDL 数据集/功能的竞争产品，并对 Solution Provider 规定下游条款传递义务。仅在确认本部署为本企业内部 CRM、无外部终端用户访问/数据再分发，并取得适用订阅计划及 Acceptable Data Use Policy 的书面确认后评估；否则须签订明确授权的 Solution Provider/数据许可协议。默认关闭。
| Hunter.io | 域名邮箱发现/验证；需 Key/额度 | 已实现待验收 | 新增 `hunter` Discover Provider：POST `/v2/discover?limit=100`，Key 通过 `X-API-KEY` header 发送；单次最多 100 家企业；只保存企业域名/名称和邮箱数量统计，不拉取/保存联系人邮箱，不使用 Premium offset 分页。官方文档：403 为请求频率限制（cooldown），429 为账户使用额度超限（不可重试直到额度恢复/套餐调整）；满页标记 incomplete。2026-08 Content Policy 允许合法 B2B prospecting/CRM enrichment，但禁止垃圾邮件、无相关性的自动群发、B2C 联系和未经书面授权的数据转售/第三方披露。

### F. AI 辅助搜索与人工辅助入口

| 来源/能力 | 类别 | 目标状态 | 后续工作 |
|---|---|---|---|
| AI Search provider | 已配置 AI 模型辅助发现 | 未迁移/需接入现有 AI 框架 | AI 结果必须标记 assisted，不可伪装官方证据；记录模型/提示词版本 |
| CanadaBuys / KONEPS / SBIR 等 assisted sources | 辅助来源目录 | 需拆分确认 | 先区分可自动调用 API、公开下载数据和人工检索链接 |
| 网站发现、AI 网站发现 | 来源发现/验证流程 | 未迁移 | 独立迁移；对目标网站请求实施 SSRF 防护和人工审核 |
| 深度挖掘 / Super Search | 多阶段搜寻编排 | 未迁移 | 迁移策略、子任务、结果合并和预算，不把它们计为单一数据源 |

## 3. 目标端当前已确认的真实 Provider 源码

截至本台账日期（本轮提交后），目标 `kiger-dev` 已确认存在以下真实适配器/解析器（均为源码存在、待运行验收）：

- `usaspending_awards`
- `uk_find_tender`
- `uk_contracts_finder`
- `eu_ted`
- `gleif`（身份解析/补充，不应错误计作通用搜客来源）
- `world_bank_procurement`（新增；公开采购公告适配器，单页 bounded fetch，存在后续页时标记不完整）
- `brazil_pncp`（新增；巴西官方采购 API，最多 5 页 bounded fetch）
- `singapore_gebiz`（新增；新加坡政府授标开放数据集，最多 5 页 bounded fetch）
- `un_comtrade`（新增；公开预览无 Key，或使用订阅 Key 的官方贸易统计 API）
- `us_census_trade`（新增；需要 Census API Key，从当前租户加密配置读取）
- `sam_gov`（新增；SAM.gov 官方 Opportunities v2，需租户 API Key，最多 3 页 bounded fetch）
- `open_fda_510k`（新增；openFDA 510(k) 医疗器械监管记录，租户 API Key 可选；仅美国医疗器械行业活动可调用，最多 3 页×100 条）
- `nppes`（新增；CMS NPPES v2.1 Type 2 组织名录；仅美国医疗行业活动可调用，最多 2 关键词×2 页×200 条；不采集个人 Type 1）\n- `sec_edgar`（新增；SEC EDGAR 官方 company_tickers.json；仅美国目标市场，Fair Access User-Agent，CIK/Ticker 身份线索，最多 100 条）
- `hunter`（新增；Hunter Discover B2B 企业发现，租户 Key，单页最多 100 家；仅记录公司域名/名称和邮箱数量，不拉取联系人邮箱）
- `companies_house`（新增；英国官方公司登记搜索，租户 API Key 通过 HTTP Basic 认证，最多 2 页 bounded fetch）
- `kvk`（新增；荷兰 KVK 官方 Handelsregister Zoeken API，租户 API Key 通过 `apikey` 请求头发送，最多 3 个关键词×每词 2 页 bounded fetch）
- `brave`（新增；Brave Web Search API，租户订阅 Token 通过 `X-Subscription-Token` 请求头发送，最多 2 页×20 条，按 `more_results_available` 标记覆盖状态）
- `ror`（新增；公开 ROR v2 organizations API，最多 3 个关键词、每个 20 条，研究机构身份记录，不作为一般商业公司库）
- `wikidata`（新增；公开 Wikidata `wbsearchentities` API，最多 3 个关键词、每个 10 条，组织描述筛选，结果标记知识图谱线索）

它们均为**已实现待验收**，不是“运行已通过”。Fake Provider 不计入真实来源数量。

## 4. 分批迁移顺序（P0）

### P0-A：来源目录与统一运行能力
- 建立 Provider Catalog：稳定代码、名称、来源类别、来源权威级别、地区/时间覆盖、能力、认证要求、启用状态、费用/配额、条款链接、字段权威等级。
- 配置按租户隔离；密钥加密保存、不回显；付费来源默认关闭。
- 统一分页/游标、结果完整性、失败类别、重试时间、配额/成本元数据、请求账本。
- 所有外部请求走统一安全执行边界；固定官方 API 端点必须 allowlist、DNS/IP pinning、禁自动重定向、响应大小上限和超时。

### Eurostat Comext 接口核验（2026-11-05）

- DS 前缀 Comext 数据集必须使用专用 API base：`https://ec.europa.eu/eurostat/api/comext/dissemination`，不能误用普通 Eurostat dissemination base。
- 已确认候选数据集 `DS-045409`（EU trade since 1988 by HS2-4-6 and CN8），维度顺序/名称包含 `freq`、`reporter`、`partner`、`product`、`flow`、`time_period`、`indicators`。
- 官方约束元数据示例显示 reporter 33 个、partner 282 个、product 40,321 个、time_period 468 个；未限制商品/伙伴/月份的请求会巨大。
- 官方同步/异步阈值：估算低于 500,000 cells 通常同步返回；500,000 至 5,000,000 可能异步；超过 5,000,000 会被拒绝。官方建议单次只发一个提取请求，不并行轰击服务。
- 后续适配器必须优先把产品限定到发电机/水泵相关 HS2/HS6/CN 编码、Reporter 限定目标 EU 国家、Partner 限定世界或必要贸易伙伴、月份限定近期窗口，并只取价值指标；商品编码与 flow 代码需在实际请求前从官方 codelist/数据约束中逐项核实。
- 当前 Provider contract 是单次同步 `SearchAsync`，没有异步 extraction key/status/data continuation；在未扩展通用运行时之前，不接入可能返回异步 SOAP 状态的宽查询，避免把“已提交后台任务”误判为数据结果。

### P0-B：官方采购与贸易信号
- 补齐 World Bank Procurement、SAM.gov、PNCP、GeBIZ。
- 补齐 UN Comtrade、US Census Trade、Eurostat Comext。
- 每个来源都要能输出采购方/贸易主体的证据语义，不能把统计聚合数据直接当成具体进口商。

### P0-C：官方企业身份与注册
- Companies House 已有真实 Provider 源码，进入来源级测试与实机验收；继续补齐 KVK、OpenCorporates、Wikidata、法国企业名录、ROR 等；NPPES 与 FDA 510(k) 已有适配器源码，待运行验收。
- 为每个来源标注适用行业、国家覆盖和权威等级；身份冲突不得静默覆盖。

### P0-D：通用发现与商业补全
- 已新增 Serper、Brave Search、SerpApi Provider；Google Places 仍待迁移。
- Apollo、PDL、Hunter 作为可选商业补全源，必须租户显式启用、提供凭证并确认配额/成本；默认关闭。
- AI 搜索、网站发现和深度挖掘作为编排能力单独验收。

## 4.1 API Key 前端录入入口（本轮新增）

获客工作台新增 `ProviderCredentialsPanel.vue`，可为需要凭证的数据源录入/轮换/清除 API Key 或 Token。当前首批入口包括 Serper、Brave Search、SerpApi、Google Places、SAM.gov、UN Comtrade、US Census、Apollo、People Data Labs、Hunter、OpenCorporates、Netherlands KVK、Companies House、openFDA 510(k)。

- 前端通过现有系统配置 API 写入 `CRM_PROSPECTING_PROVIDER` 配置组，按当前租户保存，配置项标记 `ConfigType.Tenant` 与 `isEncrypted=true`，由现有 SaaS 配置密文保护机制加密保存。
- 列表仅使用 `hasCurrentValue` 显示是否已配置；不读取或回显密钥明文。
- 修改时留空表示不覆盖；显式清除通过删除旧租户密钥行并重建启用状态的加密空值 tombstone，防止误回退到平台级 Key（加密配置的普通空值更新语义是保留旧密钥）。
- 新建凭证配置默认 Disabled；保存凭证不等于启用 Provider，不会自动触发外部调用。
- 该面板完成的是安全录入与密文存储入口。各 Provider 从租户/系统配置动态取密钥、凭证轮换后热更新以及租户隔离读取，仍需逐项接入并测试；在此之前不能把“前端已可录入”计作“数据源已可运行”。



### 本轮新增贸易数据源接入说明

- UN Comtrade：`EnableUnComtradeProvider` 显式开启；无 Key 时调用官方 public preview（500 行上限），提供订阅 Key 时切换到 `/data/v1/get`。目标市场只支持适配器内明确映射的 M49 Reporter 国家；不支持的国家返回清晰 warning，不猜测代码。
- US Census：`EnableUsCensusTradeProvider` 显式开启；默认关闭。Key 从当前租户加密 SaaS 配置读取；只请求官方 HS 出口数据集的有效字段。官方 API 要求 Key 作为 `key` 查询参数，需在 HTTP 诊断中脱敏。
- SAM.gov：`EnableSamGovProvider` 显式开启；默认关闭。Key 从当前租户加密 SaaS 配置读取，按官方要求作为 `api_key` 查询参数发送；请求窗口限制为 30 天、最多 3 页，达到页数上限会标记 coverage incomplete。
- ROR：`EnableRorProvider` 显式开启；默认关闭，无需 API Key。使用官方 `GET /v2/organizations?query=...&page=1`；每次关键词只取第 1 页（最多 20 条），最多 3 个关键词；`metadata.number_of_results` 大于 20 时标记 coverage incomplete。只用于高校、科研机构、研究设施等机构身份辅助；不应把 ROR 覆盖不足误判为企业不存在，也不能把研究机构记录当成一般采购商。
- Wikidata：`EnableWikidataProvider` 显式开启；默认关闭。无需 API Key；通过官方 MediaWiki `wbsearchentities` 搜索接口查询最多 3 个关键词，每个关键词最多 10 条，发送明确 User-Agent；仅保留描述中出现 company、manufacturer、supplier、distributor、organization 等组织线索的实体，单词条命中 10 条上限即标记 coverage incomplete。该来源是知识图谱发现线索，可能存在过期、误分类和描述语言偏差，必须人工核验。
- Companies House：`EnableCompaniesHouseProvider` 显式开启；默认关闭。Key 从当前租户加密 SaaS 配置读取，按官方要求作为 HTTP Basic 用户名发送（密码为空）；只在目标市场明确包含 UK/GB/United Kingdom 等英国标识时调用，搜索结果是公司登记证据而非采购意向，最多 2 页并标记未覆盖的后续结果。
- **租户凭证链路已接通（待测试）：**相关 Provider 均为 Scoped，后台 Worker 先切换到 Run 的 Tenant Scope，再解析 `ISaasConfigurationService`；分别读取 `CRM.Prospecting.Provider.un_comtrade.ApiKey`、`CRM.Prospecting.Provider.us_census.ApiKey`、`CRM.Prospecting.Provider.sam_gov.ApiKey` 与 `CRM.Prospecting.Provider.companies_house.ApiKey`，复用 SaaS 的加密配置解密与租户优先级/缓存逻辑。Comtrade Key 通过 `Ocp-Apim-Subscription-Key` 请求头发送；Census 按官方要求使用 `key` 查询参数；SAM.gov 使用 `api_key` 查询参数；Companies House 使用 HTTP Basic 用户名。诊断脱敏器现覆盖通用 `key`、`api_key`、订阅 Key 和 Basic/Bearer 认证文本；仍需实机检查应用日志、HTTP handler 诊断与 APM 遥测是否存在额外的请求头/查询串采集。真实多租户隔离和 Data Protection 密钥环仍待实机验收。

## 4.2 Companies House 公司登记 Provider（本轮新增）

- 使用官方 `GET /search/companies` 接口；按 `q` 搜索产品/行业关键词，`items_per_page=100`，最多请求两页。
- API Key 通过 HTTP Basic Authentication 的用户名字段发送，密码为空；Key 仅从当前租户加密 SaaS 配置读取，不进入 URL。
- 仅在目标市场明确包含 UK、GB、United Kingdom、Great Britain 或英国组成地区时调用。
- 输出公司编号、名称、登记状态、公司类型、登记地址摘要及官方公司详情 URL；不推断采购意向、不生成联系人或邮箱。
- 达到两页上限仍有更多匹配项时，`IsComplete=false` 并返回 warning。官方 API 每应用默认限额为 5 分钟 600 次，Provider 仍按小页数 bounded fetch 控制单次任务。

## 4.3 Netherlands KVK 企业登记 Provider（本轮新增）

- 使用官方 `GET https://api.kvk.nl/api/v2/zoeken`（Handelsregister Zoeken）；按 `naam` 搜索产品/行业关键词，使用 `pagina` 与 `resultatenPerPagina` 分页，每页最多 100 条。
- 需要 KVK Developer Portal 审批的 API subscription 与租户 API Key；按官方 Quickstart 使用 `apikey` 请求头，不把 Key 放入 URL。Key 从当前租户加密 SaaS 配置读取：`CRM.Prospecting.Provider.kvk.ApiKey`。
- 仅在目标市场明确包含 NL、NLD、Netherlands、Holland 或 Nederland 时调用；最多 3 个关键词、每词最多 2 页；若官方总数显示仍有未读取结果，则 `IsComplete=false`。
- 输出 KVK 编号、登记名称、活跃状态、登记类型、分支号及地址摘要；不输出联系人、不推断采购意向，也不把登记信息自动写入正式 Lead。
- 官方资料说明 Zoeken API 的搜索结果默认排除非活跃登记，最多 100 条/页；KVK API 订阅有固定月费和按次查询费用，费用与申请资格须由用户在 KVK Developer Portal 确认。来源：KVK Quickstart、Zoeken API 与订阅说明。

## 4.4 Brave Search 网页发现 Provider（本轮新增）

- 使用官方 Web Search API：`GET https://api.search.brave.com/res/v1/web/search`；API Token 通过 `X-Subscription-Token` 请求头发送，不进入 URL。Key 从当前租户加密 SaaS 配置读取：`CRM.Prospecting.Provider.brave.ApiKey`。
- `EnableBraveSearchProvider` 默认关闭；显式启用后最多执行 2 页，每页最多 20 条，offset 从 0 开始递增；优先遵循响应 `query.more_results_available`，缺失该字段但返回满页时保守处理为可能不完整。
- 根据已识别的目标市场映射 Brave `country` 参数；无法识别时会给出提示，不伪造国家映射。查询词由产品范围、理想客户画像、搜索目标与目标市场组合，限制在 API 的 600 字符范围内。
- 只接受 HTTPS 搜索结果链接，应用 ExcludedDomains 排除域名，并按规范化 URL 去重。输出标题、摘要、来源 URL 与网站主机；`EvidenceType=web_search_result`，不把网页标题推断为已验证公司名称，也不提取/验证联系人。
- Brave 官方文档规定 Token 通过 `X-Subscription-Token` 头传递，单页最多 20 条、offset 最大 9，并建议使用 `more_results_available` 判断是否继续分页。Search API 按请求计费，当前官方 Search 计划标价为每 1,000 次请求 5 美元并包含每月 5 美元免费额度；实际套餐与条款以账户页面为准。

## 4.5 Serper Google Search Provider（本轮新增）

- 使用 `POST https://google.serper.dev/search`，租户 Key 通过 `X-API-KEY` Header 发送；请求 JSON 包含 `q`、`num=20`，可识别的目标市场映射到 `gl`。Key 从租户加密 SaaS 配置 `CRM.Prospecting.Provider.serper.ApiKey` 读取。
- `EnableSerperProvider` 默认关闭；单次请求最多 20 条，不进行未经官方合同确认的深分页。返回满 20 条时保守标记 `IsComplete=false`，避免误报搜索覆盖完整。
- 仅接受 HTTPS organic link，排除 ExcludedDomains，按规范化 URL 去重；`EvidenceType=web_search_result`，不把搜索结果直接视为已验证公司或采购意向。
- 参考 API 合同：Serper Google Search 使用 `POST /search`、`X-API-KEY`、JSON `q`/`num` 与 `organic[].title/link/snippet`。可参考公开 API 说明与非官方实测参考，真实额度和计费以 Serper 控制台为准。

## 4.6 SerpApi Google Search Provider（本轮新增）

- 使用 `GET https://serpapi.com/search`，参数包含 `engine=google`、`q`、`num=10`、`start`、可选 `gl`/`hl`。SerpApi 官方合同要求 API Key 通过 `api_key` query parameter 传递，因此本 Provider 不将 URL 写入 evidence，不输出请求 URL；所有 HTTP 诊断必须删除完整 query string。
- Key 从租户加密 SaaS 配置 `CRM.Prospecting.Provider.serpapi.ApiKey` 读取；`EnableSerpApiProvider` 默认关闭。最多 2 页，每页 10 条；只在 `serpapi_pagination.next` 存在时请求下一页，超过上限标记 `IsComplete=false`。
- 只接受 HTTPS `organic_results`，过滤 ExcludedDomains，按规范化 URL 去重；`EvidenceType=web_search_result`。API Key 进入 URL 是上游合同限制，建议优先使用 Serper/Brave 等 Header 认证来源，并在实机环境检查 HttpClient/代理/异常日志的 URL 脱敏。
- 官方参数与分页说明：SerpApi Search API、Pagination 文档。

## 4.7 France Government Company Search Provider（本轮新增）

- 使用法国政府公开 API `GET https://recherche-entreprises.api.gouv.fr/search`，无需 API Key；请求包含 `q`、`page`、`per_page=25`、`minimal=true` 与 `include=siege`。只在目标市场明确包含 FR/FRA/France/French Republic 时调用。
- 最多 3 个产品/ICP/搜索目标关键词，每个关键词最多 2 页；请求串行执行，间隔 200ms，低于官方 7 req/s/IP 上限。遇到 429/503 按 `Retry-After` 返回限流异常。
- 只接受 9 位数字 SIREN 和非空企业名称；映射 SIRET、注册地址城市/邮编、NAF/APE 与行政状态为 `official_company_registry` 证据。只保存政府目录企业详情 URL，不抓取自然人负责人信息、不提取邮箱电话、不推断采购意向。
- API 的边界必须保留：该搜索接口并非完整 Sirene 数据库；不可查询非公开传播企业和拒绝 RCS 注册的企业。结果覆盖不完整时由 Provider 标记 `IsComplete=false`。
- 官方资料：API Recherche d’entreprises 文档说明开放访问、搜索参数、每页最多 25 条、7 req/s/IP 与 429/Retry-After；data.gouv.fr 说明其为 DINUM 公开企业搜索 API，返回企业名称、SIREN、SIRET、NAF 等字段。

## 5. 业务与安全边界

- 数据源只能生成 Candidate / Evidence，不得直接创建或更新正式 CRM Lead。
- Candidate 必须经人工审核后才能转入目标项目已有 CRM。
- 不自动发送开发邮件、WhatsApp 或其他外联。
- 不绕过登录、付费墙、CAPTCHA 或其他访问控制。
- AI 推断的邮箱、电话、身份和采购意向不得标记为已验证。
- 不假设用户已购买任何付费数据源，也不预设任何 API Key。
- 所有统计贸易来源必须保留聚合属性，不能误称为某家公司真实进口记录。

## 6. 验收状态

| 项目 | 当前状态 |
|---|---|
| 源项目来源盘点 | 已建立基线，后续按源代码变化更新 |
| 目标端 Provider 对照 | 已建立首轮映射 |
| 真实来源适配器覆盖 | 不完整 |
| 统一目录/租户配置/成本配额 | 待完善 |
| 全来源契约测试 | 未完成 |
| 真实 API 冒烟测试 | 未完成 |
| Windows 原生 .NET 构建/测试 | 未完成 |
| PostgreSQL + Redis 端到端验收 | 未完成 |

**总判定：P0 未完成。** 这份台账将作为后续每一批 Provider 迁移、代码审查和最终 100% 验收的核对基准。


## 4. 新增来源接口核验（2026-11-05）

### Google Places API (New)

- 使用 Text Search (New)，不使用已弃用/旧版 Places 搜索端点。
- `FieldMask` 是必填项；禁止生产环境通配符 `*`，避免意外升级 SKU 和过度采集。
- `places.id` 可作为稳定来源键；电话、网站属于较高价格层级字段，按需分阶段补取，不能默认每次搜索都请求。
- 单页最多 20 条，跨页最多 60 条；超过本 Provider 预算时必须标记 `IsComplete=false`。
- 位置偏置不能代替目标市场过滤；返回记录必须按目标国家/地区二次核验。
- API Key 不进入 URL、证据链接或日志；使用 `X-Goog-Api-Key` header、固定 host、关闭重定向、租户加密配置。
- 官方接口参考：Google Places Text Search (New)、Field Masks、Usage and Billing。

### NPPES 与 openFDA 适用性

- NPPES 是医疗行业专用身份目录，包含自然人和机构；不得把个人医疗从业者无差别加入普通 B2B 获客名单。优先 Type 2 组织实体，并将 NPI 仅作为登记标识，不代表执照有效或专业资质已核验。
- openFDA 510(k) 记录的是器械上市前通告申请及申请人/器械信息；适合医疗器械行业的产品和申请人线索，不是一般采购商目录。
- openFDA 允许无 Key 调用，建议持续使用时申请 Key；单次 `limit` 最大 1000，`skip` 最大 25000，超过范围需采用更细查询窗口或其他官方数据下载方式。
- 两者都应默认关闭，并只在医疗行业 ICP 适配时运行；不能用于 SUPERKIGER 发电机/水泵常规获客主流程。


### openFDA 510(k) Provider 实现记录（2026-11-05）

- 新增 `CrmOpenFda510kProspectingProvider`、独立契约测试、DI opt-in 注册及默认关闭配置。
- API Key 通过当前租户加密 SaaS 配置 `CRM.Prospecting.Provider.open_fda_510k.ApiKey` 读取；不硬编码、不回显。openFDA 无 Key 可用，Key 为可选增强额度。
- 只允许美国目标市场；活动的产品范围/ICP/目标必须包含医疗器械、医疗设备、临床、诊断、外科或医院等明确医疗语义。普通发电机、水泵获客不会调用此来源。
- 按器械名称关键词查询，最多 3 个关键词×3 页×100 条；稳定键为 K 编号；保留 applicant/device/product code/decision date；证据类型 `medical_device_regulatory_record`。
- 不把 510(k) 清关记录解释成采购意向；不自动创建 CRM Lead、不自动发送邮件。
- 当前状态为源码已实现待运行验收；尚未执行 .NET 构建、单元测试或真实 openFDA API 冒烟。


### NPPES Type 2 Provider 实现记录（2026-11-05）

- 新增 `CrmNppesOrganizationProspectingProvider`、独立契约测试、DI opt-in 注册及默认关闭配置。
- 仅使用 CMS NPPES API v2.1；固定 `npiregistry.cms.hhs.gov` host；不需要 API Key。
- 只在美国目标市场且活动明确涉及医疗/健康/临床/医院/诊断/牙科/药房/实验室等领域时执行；查询强制 `enumeration_type=NPI-2`，过滤响应中的非 Type 2 记录。
- 每个 Run 最多 2 个关键词×2 页×200 条，页满到达边界即标记 coverage incomplete；不会把受限结果标记为完整。
- 输出 NPI、机构名称、主 taxonomy、官方记录 URL、更新时间与 SHA-256 摘要；不提取/存储个人 NPI-1 记录，不把 NPI 颁发解释为执照或资质认证。
- CMS Registry 自 2024-06-25 起限制每小时查询量，未公布固定额度；批量检索须使用 CMS DDS 下载文件，不得通过 API 高频抓取。
- 当前源码与测试待运行验收；尚未执行 .NET build/test 或真实 NPPES API 冒烟。

Google Places 的接口和数据政策边界也已再次核验：官方政策明确 Place ID 可长期保存，但其他 Places 内容受缓存/存储限制。当前 CRM Provider 会持久化搜索结果，因此先不接入该来源，避免把名称、地址等受限内容写入 CRM。若未来实现 Place ID-only 存储 + 按需实时展示（并提供要求的 Google attribution/Terms/Privacy），再评估启用。


## 6. 商业数据源许可决策记录（2026-11-05）

本项目的 CRM 具备多租户能力，但这不代表可以默认把供应商数据作为可供任意租户/外部客户访问的数据服务。商业数据源统一按“谁持有订阅、谁能访问数据、数据是否对第三方可见、是否允许写入 CRM、是否允许长期保留/导出”五项核验。

### Apollo.io
- API Terms 页面当前标注版本日期 2024-08-13，API License 限于 internal business purposes；禁止第三方访问、转授权/分发和未授权集成。
- Apollo 2026 Terms of Service 对订阅期间导入自有 CRM/系统/工作流的 Business Contact Information 给予 internal business purposes 范围内的持续使用许可；但仍禁止向第三方提供 Contributor Database 或服务数据。
- 实施门槛：只有单一企业内部部署、授权员工/承包商访问、无外部客户/租户使用、无数据再销售或共享，并且 API Terms、当前订单/主服务协议均覆盖该用途，才可考虑适配器；否则先取得 Apollo 书面许可。不可把“前端有 Key 输入框”当作已获许可。

### People Data Labs
- PDL Subscription Agreement/Acceptable Data Use Policy 对 Data 再销售、再许可、向 End Users 暴露实质性数据集/功能有严格限制；Solution Provider 可能需要向下游合同传递同等限制。
- 实施门槛：先确认仅限本企业内部 CRM 的使用范围和订阅权限；任何多租户外部客户访问、数据导出/转供均需 PDL 明确书面许可及相应合同。未确认前不开发、不调用。

### Hunter.io
- Hunter 2026-08 Content Policy 明确允许正当 B2B prospecting、专业身份联系和 CRM enrichment；同时明确禁止垃圾邮件、无相关性的自动群发、B2C 联系及未经书面授权的第三方转售/披露。
- 可作为下一批优先 Provider，但只实现专业域名发现/邮箱补全/验证，不自动外联；需保留来源、email 类型（public/inferred）、验证时间和抑制名单状态；数据最小化，按实际套餐的 credits/rate limit 限制调用。

### 共同安全与产品约束
- 所有商业来源默认关闭；API Key 只从当前租户加密配置读取，不回显、不写日志。
- 每次调用前校验来源开关、当前租户凭证、地区/用途条件和预算；不能把缺少凭证当作静默成功。
- 不将供应商个人联系人数据用于 AI 模型训练；不得把供应商数据向未授权租户或外部客户开放。
- 外发邮件和网站发布继续要求人工审批；供应商邮箱验证成功不等于获得发送许可。
- 许可未通过时，保留来源台账和前端凭证入口可以，但 Provider 必须不存在或保持不可执行状态。


### Hunter Discover Provider 实现记录（2026-11-05）

- 新增 `CrmHunterDiscoverProspectingProvider`、独立契约测试、DI opt-in 注册及默认关闭配置。
- 通过当前租户加密配置 `CRM.Prospecting.Provider.hunter.ApiKey` 读取 Key；API 使用 POST `https://api.hunter.io/v2/discover?limit=100`，Key 通过 `X-API-KEY` header 发送，不进入 URL。
- 搜索使用活动 ICP、产品范围、目标市场和搜索目标组成自然语言 query；固定最多 100 条单页，不依赖 Premium 才能使用的 offset 分页。
- 只保存公司名称、域名、网站和邮箱数量统计；不会在 Discover 阶段暴露或保存个人邮箱/联系人，也不会触发 Hunter Sequences 或 CRM 自动外联。
- 过滤无效域名和租户排除域名；固定 host、HTTPS、响应 8 MB 上限、429/503 cooldown；诊断脱敏器已覆盖 `api_key` 查询参数。
- 返回满 100 条或 meta 仍显示更多结果时，必须将 coverage 标记 incomplete。
- 当前源码和测试已提交，尚未运行 .NET build/test 或真实 Hunter API 冒烟；API Discover 权限/套餐、调用限额和最终用户数据使用范围需由实际 Hunter 账号确认。
- 限流语义复核：Hunter 403 按请求频率限制进入 cooldown；503 按临时故障进入 cooldown；429 是账户使用额度耗尽，记为普通 HTTP 失败，不进入自动重试/cooldown。2026-11-05 已修正测试名称/断言，避免把 429 配额错误当作短时限流。
