namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Static capability and licence metadata for prospecting data sources.
/// Evidence level A: every <see cref="Code"/> matches an <c>ICrmProspectingProvider.ProviderCode</c>
/// in <c>Infrastructure/Prospecting</c>, and every <see cref="ConfigKey"/> matches the key read by
/// <c>CrmProspectingProviderRegistrationExtensions</c>. Runtime availability is deliberately NOT
/// declared here: the catalog query service merges this list with <see cref="CrmProspectingProviderRegistry"/>.
/// </summary>
/// <param name="Code">Stable provider code used in persisted strategy plans.</param>
/// <param name="Label">Human-readable source name.</param>
/// <param name="Group">Machine-readable catalog group.</param>
/// <param name="NeedsKey">Whether tenant credentials are normally required.</param>
/// <param name="KeyOptional">Whether the source also works without a key (rate limited).</param>
/// <param name="ConfigKey">Server opt-in configuration key that registers the adapter.</param>
/// <param name="Note">Honest capability, coverage and licence restriction.</param>
public sealed record CrmProspectingProviderDefinition(
    string Code,
    string Label,
    string Group,
    bool NeedsKey,
    bool KeyOptional,
    string ConfigKey,
    string Note);

/// <summary>Authoritative static metadata list. Adding an adapter requires adding a row here.</summary>
public static class CrmProspectingProviderCatalog
{
    public static IReadOnlyList<CrmProspectingProviderDefinition> All { get; } =
    [
        new("serper", "Serper Google 搜索", "search", true, false, "EnableSerperProvider", "按 credits 计费，结果为网页发现线索"),
        new("brave", "Brave Web Search", "search", true, false, "EnableBraveSearchProvider", "需订阅 Token，最多 2 页"),
        new("serpapi", "SerpApi Google 搜索", "search", true, false, "EnableSerpApiProvider", "按套餐计费，最多 2 页"),

        new("companies_house", "英国 Companies House", "registry", true, false, "EnableCompaniesHouseProvider", "英国官方公司登记，HTTP Basic 凭证"),
        new("kvk", "荷兰 KVK", "registry", true, false, "EnableKvkProvider", "荷兰官方企业登记，需订阅 Key"),
        new("fr_company_search", "法国政府企业目录", "registry", false, false, "EnableFranceCompanySearchProvider", "免 Key，仅法国市场，SIREN/SIRET 身份线索"),
        new("sec_edgar", "美国 SEC EDGAR", "registry", true, false, "EnableSecEdgarProvider", "非 API Key，需填写系统名 + 联系邮箱作为 User-Agent"),
        new("ror", "ROR 研究机构标识", "registry", false, false, "EnableRorProvider", "免 Key，研究机构身份，不作普通公司库"),
        new("wikidata", "Wikidata 组织实体", "registry", false, false, "EnableWikidataProvider", "免 Key，知识图谱描述线索，非企业注册"),
        new("nppes", "美国 NPPES 组织名录", "registry", false, false, "EnableNppesProvider", "免 Key，仅美国医疗行业、NPI-2 组织记录"),
        new("open_fda_510k", "openFDA 510(k)", "registry", true, true, "EnableOpenFda510kProvider", "Key 可选；仅美国医疗器械活动，为监管证据而非采购意向"),

        new("eu_ted", "欧盟 TED 招标公告", "procurement", false, false, "EnableTedProvider", "免 Key，欧盟官方招标公告"),
        new("uk_find_tender", "英国 Find a Tender", "procurement", false, false, "EnableFindTenderProvider", "免 Key，英国官方招标公告"),
        new("uk_contracts_finder", "英国 Contracts Finder", "procurement", false, false, "EnableContractsFinderProvider", "免 Key，英国公共采购公告"),
        new("usaspending_awards", "美国 USAspending", "procurement", false, false, "EnableUsaSpendingProvider", "免 Key，美国联邦合同 / 支出记录"),
        new("world_bank_procurement", "世界银行采购公告", "procurement", false, false, "EnableWorldBankProcurementProvider", "免 Key，单页上限 20 条"),
        new("brazil_pncp", "巴西 PNCP", "procurement", false, false, "EnableBrazilPncpProvider", "免 Key，巴西国家公共采购门户"),
        new("singapore_gebiz", "新加坡 GeBIZ", "procurement", false, false, "EnableSingaporeGebizProvider", "免 Key，历史授标记录而非开放招标"),
        new("sam_gov", "美国 SAM.gov 机会", "procurement", true, false, "EnableSamGovProvider", "需官方 API Key，最近 30 天窗口、最多 3 页"),

        new("un_comtrade", "UN Comtrade", "trade", true, true, "EnableUnComtradeProvider", "Key 可选；结果为聚合贸易信号，不是具体进口商"),
        new("us_census_trade", "美国 Census 贸易统计", "trade", true, false, "EnableUsCensusTradeProvider", "需 Census API Key，HS2 出口统计信号"),

        new("hunter", "Hunter Discover", "enrichment", true, false, "EnableHunterDiscoverProvider", "仅发现企业域名与邮箱数量，不拉取联系人邮箱"),
        new("mexico_denue", "墨西哥 INEGI DENUE", "enrichment", true, false, "EnableMexicoDenueProvider", "路径 Token，全链路脱敏验证未完成前默认不注册"),
        new("fmcsa_qcmobile", "美国 FMCSA QCMobile", "enrichment", true, false, "EnableFmcsaQcmobileProvider", "仅承运商目录，WebKey 查询参数需脱敏验证"),
    ];
}
