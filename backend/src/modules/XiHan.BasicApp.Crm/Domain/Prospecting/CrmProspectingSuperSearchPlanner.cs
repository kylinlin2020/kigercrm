using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Bounded depth profiles for multi-round prospect discovery.</summary>
public enum CrmProspectingSuperSearchDepth
{
    Balanced = 0,
    Deep = 1,
    Extreme = 2
}

/// <summary>Immutable, validated input for a deterministic Super Search preview.</summary>
public sealed record CrmProspectingSuperSearchInput(
    IReadOnlyList<string> Products,
    IReadOnlyList<string> Markets,
    IReadOnlyList<string> CustomerTypes,
    IReadOnlyList<string> Industries,
    IReadOnlyList<string> ProviderCodes,
    IReadOnlyList<string> ExcludedTerms,
    IReadOnlyList<string> ExcludedDomains,
    CrmProspectingSuperSearchDepth Depth,
    int? RequestedRounds = null);

/// <summary>One provider-specific query cell within a planned round.</summary>
public sealed record CrmProspectingSuperSearchQueryCell(
    string ProviderCode,
    string Market,
    string CustomerType,
    string ThemeCode,
    string QueryText,
    string Fingerprint);

/// <summary>A deterministic search round. Exclusions are carried into every round.</summary>
public sealed record CrmProspectingSuperSearchRoundPlan(
    int RoundNumber,
    string ThemeCode,
    string ThemeLabel,
    string Market,
    string CustomerType,
    IReadOnlyList<string> ExcludedTerms,
    IReadOnlyList<string> ExcludedDomains,
    IReadOnlyList<CrmProspectingSuperSearchQueryCell> QueryCells,
    string Fingerprint);

/// <summary>Preview result containing the bounded round plans and aggregate limits.</summary>
public sealed record CrmProspectingSuperSearchPlan(
    string ScopeFingerprint,
    int MaximumRounds,
    int PlannedRounds,
    int ProviderCount,
    int TotalQueryCells,
    IReadOnlyList<CrmProspectingSuperSearchRoundPlan> Rounds);

/// <summary>
/// Pure planner for multi-round prospect discovery. It never calls providers, changes Campaign/Strategy
/// state, raises budgets, or creates CRM records. Execution must be delegated to the existing Run pipeline.
/// </summary>
public static class CrmProspectingSuperSearchPlanner
{
    private sealed record Theme(string Code, string Label, string[] Terms);

    private static readonly Theme[] Themes =
    [
        new("baseline", "基础覆盖", ["manufacturer", "distributor", "importer", "supplier"]),
        new("local_channel", "当地语言渠道", ["local distributor", "regional dealer", "sales partner"]),
        new("procurement", "采购与招标", ["tender", "procurement", "request for quotation", "contract award"]),
        new("project_engineering", "项目与工程", ["project contractor", "engineering company", "EPC", "system integrator"]),
        new("trade_channel", "进口与批发渠道", ["importer", "wholesaler", "trade data", "customs"]),
        new("oem_integration", "OEM 与系统集成", ["OEM", "equipment integrator", "industrial solutions"]),
        new("vendor_registration", "供应商准入", ["approved vendor", "supplier registration", "vendor list"]),
        new("directory_association", "行业协会与目录", ["industry association", "member directory", "trade directory"]),
        new("aftermarket_service", "售后与 MRO", ["maintenance", "repair", "MRO", "service provider"]),
        new("contract_award", "中标与框架合同", ["award notice", "framework agreement", "winning bidder"]),
        new("expansion_signal", "扩产与新建项目", ["new facility", "plant expansion", "capacity upgrade"]),
        new("certification_ecosystem", "认证与授权生态", ["certified partner", "authorized distributor", "certification"]),
        new("regional_long_tail", "区域长尾渠道", ["regional supplier", "local wholesaler", "specialist distributor"]),
        new("application_specialist", "应用方案商", ["application specialist", "process solution", "technical consultant"]),
        new("replacement_demand", "改造与替换需求", ["replacement project", "retrofit", "equipment upgrade"]),
        new("coverage_recovery", "覆盖缺口恢复", ["company profile", "official registry", "business directory"])
    ];

    public static CrmProspectingSuperSearchPlan Create(CrmProspectingSuperSearchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var products = Normalize(input.Products, nameof(input.Products), 30, 200);
        var markets = Normalize(input.Markets, nameof(input.Markets), 30, 120);
        var customerTypes = Normalize(input.CustomerTypes, nameof(input.CustomerTypes), 20, 160);
        var industries = Normalize(input.Industries, nameof(input.Industries), 30, 200, allowEmpty: true);
        var providers = Normalize(input.ProviderCodes, nameof(input.ProviderCodes), 30, 80)
            .Select(code => code.ToLowerInvariant()).ToArray();
        var excludedTerms = Normalize(input.ExcludedTerms, nameof(input.ExcludedTerms), 100, 200, allowEmpty: true);
        var excludedDomains = NormalizeDomains(input.ExcludedDomains);

        var maximumRounds = input.Depth switch
        {
            CrmProspectingSuperSearchDepth.Balanced => 4,
            CrmProspectingSuperSearchDepth.Deep => 8,
            CrmProspectingSuperSearchDepth.Extreme => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(input.Depth))
        };
        var roundCount = input.RequestedRounds ?? maximumRounds;
        if (roundCount is < 1 || roundCount > maximumRounds)
            throw new ArgumentOutOfRangeException(nameof(input.RequestedRounds), $"Requested rounds must be between 1 and {maximumRounds} for the selected depth.");

        var scope = new
        {
            products, markets, customerTypes, industries, providers, excludedTerms, excludedDomains,
            depth = input.Depth.ToString(), roundCount
        };
        var scopeFingerprint = Fingerprint(scope);
        var rounds = new List<CrmProspectingSuperSearchRoundPlan>(roundCount);

        for (var index = 0; index < roundCount; index++)
        {
            var theme = Themes[index % Themes.Length];
            var market = markets[index % markets.Length];
            var customerType = customerTypes[index % customerTypes.Length];
            var queryCells = providers.Select(provider =>
            {
                var queryText = string.Join(" ", products.Concat(industries).Append(customerType)
                    .Append(market).Concat(theme.Terms)).Trim();
                var cellIdentity = new { scopeFingerprint, round = index + 1, provider, market, customerType, theme = theme.Code, queryText };
                return new CrmProspectingSuperSearchQueryCell(
                    provider, market, customerType, theme.Code, queryText, Fingerprint(cellIdentity));
            }).ToArray();

            var roundIdentity = new
            {
                scopeFingerprint,
                round = index + 1,
                theme = theme.Code,
                market,
                customerType,
                excludedTerms,
                excludedDomains,
                cells = queryCells.Select(cell => cell.Fingerprint).ToArray()
            };
            rounds.Add(new CrmProspectingSuperSearchRoundPlan(
                index + 1, theme.Code, theme.Label, market, customerType,
                excludedTerms, excludedDomains, queryCells, Fingerprint(roundIdentity)));
        }

        return new CrmProspectingSuperSearchPlan(
            scopeFingerprint, maximumRounds, roundCount, providers.Length,
            rounds.Sum(round => round.QueryCells.Count), rounds);
    }

    private static string[] Normalize(
        IReadOnlyList<string>? values,
        string parameterName,
        int maxItems,
        int maxLength,
        bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > maxItems) throw new ArgumentOutOfRangeException(parameterName);
        var result = values.Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Select(value => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!allowEmpty && result.Length == 0)
            throw new ArgumentException("At least one non-empty value is required.", parameterName);
        if (result.Any(value => value.Length > maxLength || value.Any(char.IsControl)))
            throw new ArgumentException("Values exceed the allowed length or contain control characters.", parameterName);
        return result;
    }

    private static string[] NormalizeDomains(IReadOnlyList<string>? values)
    {
        var domains = Normalize(values, nameof(CrmProspectingSuperSearchInput.ExcludedDomains), 500, 253, allowEmpty: true);
        return domains.Select(domain => domain.Trim().TrimStart('.').TrimEnd('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string Fingerprint<T>(T value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
