using System.Globalization;
using System.Text.RegularExpressions;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>One explainable field extraction from a natural-language objective.</summary>
public sealed record CrmProspectingObjectiveMatch(string Field, string Marker, IReadOnlyList<string> Values);

/// <summary>Deterministic parse result: bounded planner input plus the evidence used to build it.</summary>
public sealed record CrmProspectingObjectiveParseResult(
    CrmProspectingSuperSearchInput Input,
    IReadOnlyList<CrmProspectingObjectiveMatch> Matches,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Bounded, deterministic natural-language objective parser. It recognises only explicit
/// <c>marker: value</c> lines from a fixed bilingual vocabulary, never invokes an LLM or a provider,
/// and rejects unknown markers, duplicate markers, free-form lines and ambiguous inline markers
/// instead of guessing. The output feeds the existing planner and must be reviewed by a human
/// before it can be saved as a mission or used to create a Run.
/// </summary>
public static class CrmProspectingObjectiveParser
{
    private const int MaximumObjectiveLength = 2000;

    // Exact labels are matched after trimming and lowercasing, so no prefix ambiguity is possible.
    private static readonly IReadOnlyDictionary<string, string> MarkerFields = BuildMarkerFields();

    private static readonly IReadOnlyDictionary<string, string> MarketAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["德国"] = "DE", ["de"] = "DE", ["germany"] = "DE", ["deutschland"] = "DE",
        ["巴西"] = "BR", ["br"] = "BR", ["brazil"] = "BR", ["brasil"] = "BR",
        ["美国"] = "US", ["us"] = "US", ["usa"] = "US", ["united states"] = "US",
        ["英国"] = "GB", ["gb"] = "GB", ["uk"] = "GB", ["united kingdom"] = "GB",
        ["法国"] = "FR", ["fr"] = "FR", ["france"] = "FR",
        ["日本"] = "JP", ["jp"] = "JP", ["japan"] = "JP",
        ["韩国"] = "KR", ["kr"] = "KR", ["south korea"] = "KR",
        ["中国"] = "CN", ["cn"] = "CN", ["china"] = "CN",
        ["印度"] = "IN", ["in"] = "IN", ["india"] = "IN",
        ["墨西哥"] = "MX", ["mx"] = "MX", ["mexico"] = "MX",
        ["越南"] = "VN", ["vn"] = "VN", ["vietnam"] = "VN",
        ["印尼"] = "ID", ["id"] = "ID", ["indonesia"] = "ID",
        ["土耳其"] = "TR", ["tr"] = "TR", ["turkey"] = "TR",
        ["加拿大"] = "CA", ["ca"] = "CA", ["canada"] = "CA",
        ["澳大利亚"] = "AU", ["au"] = "AU", ["australia"] = "AU",
    };

    public static CrmProspectingObjectiveParseResult Parse(string objective)
    {
        ArgumentNullException.ThrowIfNull(objective);
        if (objective.Length == 0) throw new ArgumentException("An objective is required.", nameof(objective));
        if (objective.Length > MaximumObjectiveLength)
            throw new ArgumentOutOfRangeException(nameof(objective), $"The objective must be at most {MaximumObjectiveLength} characters.");
        if (objective.Any(char.IsControl) && !objective.Any(ch => ch is '\r' or '\n' or '\t'))
            throw new ArgumentException("The objective contains control characters.", nameof(objective));

        var rawFields = new Dictionary<string, (string Marker, string Value)>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var rawLine in objective.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var separator = line.IndexOfAny([':', '：']);
            if (separator <= 0)
                throw new ArgumentException("Every objective line must use 'marker: value' syntax.", nameof(objective));
            var label = line[..separator].Trim().ToLowerInvariant();
            if (!MarkerFields.TryGetValue(label, out var field))
                throw new ArgumentException($"Unknown objective marker '{label}'.", nameof(objective));
            var value = line[(separator + 1)..].Trim();
            if (value.Length == 0) throw new ArgumentException($"Marker '{label}' has no value.", nameof(objective));
            if (value.Contains(':') || value.Contains('：'))
                throw new ArgumentException($"Marker '{label}' contains an ambiguous inline marker.", nameof(objective));
            if (!rawFields.TryAdd(field, (label, value)))
                throw new ArgumentException($"Marker '{label}' was provided more than once.", nameof(objective));
        }

        if (rawFields.Count == 0)
            throw new ArgumentException("The objective needs at least one explicit marker.", nameof(objective));

        var matches = new List<CrmProspectingObjectiveMatch>(rawFields.Count);
        string[] products = [], markets = [], customerTypes = [], industries = [], providers = [], excludedTerms = [], excludedDomains = [];
        var depth = CrmProspectingSuperSearchDepth.Balanced;
        int? requestedRounds = null;

        foreach (var (field, (marker, value)) in rawFields)
        {
            switch (field)
            {
                case "products":
                    products = SplitValues(value, field, marker, matches, 30, 200);
                    break;
                case "markets":
                    markets = SplitValues(value, field, marker, matches, 30, 120);
                    markets = markets.Select(market =>
                    {
                        if (MarketAliases.TryGetValue(market, out var code)) return code;
                        warnings.Add($"Market '{market}' is not a known alias and is kept verbatim.");
                        return market.ToUpperInvariant();
                    }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    UpdateMatchValues(matches, field, markets);
                    break;
                case "customerTypes":
                    customerTypes = SplitValues(value, field, marker, matches, 20, 160);
                    break;
                case "industries":
                    industries = SplitValues(value, field, marker, matches, 30, 200);
                    break;
                case "providerCodes":
                    providers = SplitValues(value, field, marker, matches, 30, 80);
                    if (providers.Any(code => !Regex.IsMatch(
                            code, "^[a-z0-9][a-z0-9_-]*$", RegexOptions.IgnoreCase)))
                        throw new ArgumentException("Provider codes may only contain letters, digits, '-' and '_'.", nameof(objective));
                    providers = providers.Select(code => code.ToLowerInvariant())
                        .Distinct(StringComparer.Ordinal).ToArray();
                    UpdateMatchValues(matches, field, providers);
                    break;
                case "excludedTerms":
                    excludedTerms = SplitValues(value, field, marker, matches, 100, 200);
                    break;
                case "excludedDomains":
                    excludedDomains = SplitValues(value, field, marker, matches, 100, 253)
                        .Select(domain => domain.Trim('.').ToLowerInvariant())
                        .Distinct(StringComparer.Ordinal).ToArray();
                    UpdateMatchValues(matches, field, excludedDomains);
                    break;
                case "depth":
                    depth = value.ToLowerInvariant() switch
                    {
                        "基础" or "balanced" or "basic" => CrmProspectingSuperSearchDepth.Balanced,
                        "深入" or "deep" => CrmProspectingSuperSearchDepth.Deep,
                        "极限" or "extreme" => CrmProspectingSuperSearchDepth.Extreme,
                        _ => throw new ArgumentException($"Unknown depth '{value}'.", nameof(objective)),
                    };
                    matches.Add(new CrmProspectingObjectiveMatch(field, marker, [depth.ToString()]));
                    break;
                case "requestedRounds":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var rounds)
                        || rounds is < 1 or > 16)
                        throw new ArgumentException("Rounds must be an integer between 1 and 16.", nameof(objective));
                    requestedRounds = rounds;
                    matches.Add(new CrmProspectingObjectiveMatch(field, marker, [rounds.ToString(CultureInfo.InvariantCulture)]));
                    break;
            }
        }

        var input = new CrmProspectingSuperSearchInput(
            products, markets, customerTypes, industries, providers,
            excludedTerms, excludedDomains, depth, requestedRounds);
        return new CrmProspectingObjectiveParseResult(input, matches, warnings);
    }

    private static string[] SplitValues(
        string value,
        string field,
        string marker,
        List<CrmProspectingObjectiveMatch> matches,
        int maxItems,
        int maxLength)
    {
        var values = value.Split([',', '，', ';', '；', '、', '|', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => string.Join(' ', item.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length == 0 || values.Length > maxItems)
            throw new ArgumentException($"Field '{field}' must contain between 1 and {maxItems} values.", nameof(value));
        if (values.Any(item => item.Length > maxLength || item.Any(char.IsControl)))
            throw new ArgumentException($"Field '{field}' contains a value that is too long or has control characters.", nameof(value));
        matches.Add(new CrmProspectingObjectiveMatch(field, marker, values));
        return values;
    }

    private static void UpdateMatchValues(List<CrmProspectingObjectiveMatch> matches, string field, IReadOnlyList<string> values)
    {
        var index = matches.FindIndex(match => match.Field == field);
        if (index >= 0) matches[index] = matches[index] with { Values = values };
    }

    private static IReadOnlyDictionary<string, string> BuildMarkerFields()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string field, params string[] labels)
        {
            foreach (var label in labels) map[label] = field;
        }
        Add("products", "产品", "products", "product");
        Add("markets", "市场", "地区", "markets", "market");
        Add("customerTypes", "客户类型", "客户", "customer types", "customer type", "customers");
        Add("industries", "行业", "industries", "industry");
        Add("providerCodes", "数据源", "providers", "provider", "渠道");
        Add("excludedDomains", "排除域名", "excluded domains", "excluded_domains");
        Add("excludedTerms", "排除", "不包括", "exclude", "excluded");
        Add("depth", "深度", "depth");
        Add("requestedRounds", "轮次", "rounds", "round");
        return map;
    }
}
