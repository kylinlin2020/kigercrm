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
| Serper（Google） | 搜索 API；需用户 Key；按量/套餐 | 未迁移 | 搜索查询映射、游标、额度、成本与搜索结果证据 |
| Brave Search | 搜索 API；需 Token | 未迁移 | 频率/额度管理、分页、来源链接与摘要证据 |
| SerpApi（Google） | 搜索 API；需 Key；付费/有限免费额度 | 未迁移 | 默认关闭，Key 与成本控制，分页和失败分类 |
| Google Places | 地图企业 API；需 Key/计费；受 Google 数据条款约束 | 未迁移 | FieldMask、分页、电话/网站字段来源、缓存与保留条款 |

### B. 官方企业身份、注册和行业名录

| 源项目来源 | 类别 / 覆盖 | 目标状态 | 后续工作 |
|---|---|---|---|
| GLEIF | 全球 LEI 法人身份 | 已实现待验收 | 核验精确 LEI/名称查询、匹配置信度和冲突处理 |
| Wikidata | 全球知识图谱实体发现；公开 API、无需 Key | 已实现待验收 | 新增低频 `wbsearchentities` 关键词发现 Provider；最多 3 个关键词×10 条；仅保留组织描述线索；上限命中标记 incomplete；不等同企业注册或采购意向 |
| OpenCorporates | 多国企业登记聚合；需核对许可/Key | 未迁移 | API 许可、覆盖国家、Key、字段权威等级 |
| Companies House | 英国官方企业登记；需租户 API Key（HTTP Basic 用户名） | 已实现待验收 | 已新增真实公司搜索 Provider；仅英国目标市场调用，最多 2 页，超限标记 coverage incomplete；公司登记不等同采购意向 |
| Netherlands KVK | 荷兰官方企业登记；订阅/Key | 未迁移 | 订阅开关、成本、名称与编号精确查询 |
| France company search（fr_company_search） | 法国企业公开登记入口 | 未迁移 | 确认真实 API 端点、数据许可和覆盖字段 |
| ROR | 全球研究机构标识；公开 REST API、无需 Key | 已实现待验收 | 新增真实 v2 organizations 查询 Provider；最多 3 个关键词×每词 20 条；记录研究机构类型/国家/网站；上限命中标记 incomplete；不作为普通公司库 |
| US NPPES | 美国医疗机构/从业者名录 | 未迁移 | 行业专用来源，明确个人数据边界 |
| FDA 510(k) | 美国医疗器械上市前通告记录 | 未迁移 | 行业专用产品/企业信号，不等同于企业注册 |
| Mexico DENUE | 墨西哥经济单位目录 | 需拆分确认 | 源项目归入公开/采购来源模块，核对接口和目标用途 |
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
| Apollo.io | 商业企业/联系人数据；需 Key/付费 | 未迁移 | 默认关闭；租户自带 Key；成本、额度、隐私和字段来源 |
| People Data Labs（PDL） | 商业企业/人员数据；需 Key/付费 | 未迁移 | 默认关闭；匹配置信度、成本和个人数据合规 |
| Hunter.io | 域名邮箱发现/验证；需 Key/额度 | 未迁移 | 只作为补全证据；不得自动发送邮件；验证状态需单独保存 |

### F. AI 辅助搜索与人工辅助入口

| 来源/能力 | 类别 | 目标状态 | 后续工作 |
|---|---|---|---|
| AI Search provider | 已配置 AI 模型辅助发现 | 未迁移/需接入现有 AI 框架 | AI 结果必须标记 assisted，不可伪装官方证据；记录模型/提示词版本 |
| CanadaBuys / KONEPS / SBIR 等 assisted sources | 辅助来源目录 | 需拆分确认 | 先区分可自动调用 API、公开下载数据和人工检索链接 |
| 网站发现、AI 网站发现 | 来源发现/验证流程 | 未迁移 | 独立迁移；对目标网站请求实施 SSRF 防护和人工审核 |
| 深度挖掘 / Super Search | 多阶段搜寻编排 | 未迁移 | 迁移策略、子任务、结果合并和预算，不把它们计为单一数据源 |

## 3. 目标端当前已确认的真实 Provider 源码

截至本台账日期（本轮提交后），目标 `kiger-dev` 已确认存在以下真实适配器/解析器：

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
- `companies_house`（新增；英国官方公司登记搜索，租户 API Key 通过 HTTP Basic 认证，最多 2 页 bounded fetch）
- `wikidata`（新增；公开 Wikidata `wbsearchentities` API，最多 3 个关键词、每个 10 条，组织描述筛选，结果标记知识图谱线索）
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
- Companies House 已有真实 Provider 源码，进入来源级测试与实机验收；继续补齐 KVK、OpenCorporates、Wikidata、法国企业名录、ROR、NPPES、FDA 510(k) 等。
- 为每个来源标注适用行业、国家覆盖和权威等级；身份冲突不得静默覆盖。

### P0-D：通用发现与商业补全
- 补齐 Serper、Brave、SerpApi、Google Places。
- Apollo、PDL、Hunter 作为可选商业补全源，必须租户显式启用、提供凭证并确认配额/成本；默认关闭。
- AI 搜索、网站发现和深度挖掘作为编排能力单独验收。

## 4.1 API Key 前端录入入口（本轮新增）

获客工作台新增 `ProviderCredentialsPanel.vue`，可为需要凭证的数据源录入/轮换/清除 API Key 或 Token。当前首批入口包括 Serper、Brave Search、SerpApi、Google Places、SAM.gov、UN Comtrade、US Census、Apollo、People Data Labs、Hunter、OpenCorporates、Netherlands KVK、Companies House。

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
