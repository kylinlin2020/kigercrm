namespace XiHan.BasicApp.Crm.Domain.Research;

/// <summary>
/// Deterministic rules for local background research: a confidence floor, bounded report sizes,
/// and a reproducible priority score derived only from real opportunity data. Nothing here calls
/// an external model or invents a fact that was absent from the source records.
/// </summary>
public static class CrmBackgroundResearchPolicy
{
    public const int MinimumConfidence = 40;
    public const int MaxEvidence = 50;
    public const int MaxOpportunities = 20;
    public const int MaxFactValueLength = 500;

    public static int ClampConfidence(int value) => Math.Clamp(value, 0, 100);

    public static bool IsIncluded(CrmResearchEvidenceDraft evidence)
        => evidence.Confidence >= MinimumConfidence
            && !string.IsNullOrWhiteSpace(evidence.FactKey)
            && !string.IsNullOrWhiteSpace(evidence.FactValue);

    /// <summary>Score in [0,100]: win probability weighted at 60%, amount at most 40 points.</summary>
    public static int OpportunityPriority(decimal amount, int? probability)
    {
        var probabilityPoints = Math.Clamp(probability ?? 0, 0, 100) * 0.6d;
        var amountPoints = Math.Clamp(amount / 1000m, 0m, 40m);
        return (int)Math.Clamp(Math.Round(probabilityPoints + (double)amountPoints), 0d, 100d);
    }

    public static string Truncate(string? value)
        => value is null
            ? string.Empty
            : value.Length <= MaxFactValueLength ? value : value[..MaxFactValueLength];
}
