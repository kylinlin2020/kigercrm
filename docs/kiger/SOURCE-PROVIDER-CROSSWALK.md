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
