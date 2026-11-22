using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Dtos;

namespace XiHan.BasicApp.Crm.Application.Services;

/// <summary>
/// Deterministic, dependency-free research report exporter. Only two explicit formats are allowed;
/// an unknown format is an error, never a silent fallback. All values come from the stored report.
/// </summary>
public static class CrmBackgroundResearchExporter
{
    public const string Markdown = "markdown";
    public const string Json = "json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string NormalizeFormat(string? format)
        => format?.Trim().ToLowerInvariant() switch
        {
            Markdown => Markdown,
            Json => Json,
            _ => throw new ArgumentException("Unsupported research export format.", nameof(format))
        };

    public static string RenderMarkdown(CrmBackgroundResearchDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var report = detail.Report;
        var text = new StringBuilder();
        text.AppendLine($"# Customer background research: {Escape(report.CustomerName)}");
        text.AppendLine();
        text.AppendLine($"Generated: {report.GeneratedAtUtc:O}");
        text.AppendLine($"Evidence: {report.EvidenceCount}, Opportunities: {report.OpportunityCount}");
        text.AppendLine();
        text.AppendLine("## Evidence");
        text.AppendLine();
        text.AppendLine("| Source | Fact | Value | Confidence |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var item in detail.Evidence)
            text.AppendLine($"| {Escape(item.Source.ToString())} | {Escape(item.FactKey)} | {Escape(item.FactValue)} | {item.Confidence} |");
        text.AppendLine();
        text.AppendLine("## Market opportunities");
        text.AppendLine();
        text.AppendLine("| Title | Fact | Priority | Status |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var item in detail.Opportunities)
            text.AppendLine($"| {Escape(item.Title)} | {Escape(item.FactValue)} | {item.PriorityScore} | {Escape(item.Status.ToString())} |");
        return text.ToString();
    }

    public static string RenderJson(CrmBackgroundResearchDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return JsonSerializer.Serialize(detail, JsonOptions);
    }

    public static string BuildFileName(long customerId, string format, DateTimeOffset nowUtc)
        => $"background-research-{customerId}-{nowUtc:yyyyMMddHHmmss}.{(format == Json ? "json" : "md")}";

    private static string Escape(string? value)
        => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
