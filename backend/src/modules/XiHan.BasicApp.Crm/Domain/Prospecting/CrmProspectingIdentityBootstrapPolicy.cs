namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Degree of confidence reached by the staged identity bootstrap.</summary>
public enum CrmProspectingIdentityBootstrapStage
{
    Unresolved = 0,
    NeedsOperatorReconciliation = 1,
    ExactRegistryMatch = 2,
}

public sealed record CrmProspectingIdentityBootstrapStep(string Stage, string ReasonCode, string Detail);

/// <summary>
/// Explainable staged bootstrap over discovered registry candidates. It never guesses: only a
/// single normalized legal-name match with a compatible country is promoted to authoritative.
/// Ambiguity is returned to the operator with the full candidate list and the reason it was blocked.
/// </summary>
public sealed record CrmProspectingIdentityBootstrapResult(
    CrmProspectingIdentityBootstrapStage Stage,
    CrmProspectingLegalEntityMatch? AuthoritativeMatch,
    IReadOnlyList<CrmProspectingLegalEntityMatch> Candidates,
    IReadOnlyList<CrmProspectingIdentityBootstrapStep> Steps);

public static class CrmProspectingIdentityBootstrapPolicy
{
    public static CrmProspectingIdentityBootstrapResult Build(
        string companyName,
        string? countryCode,
        IReadOnlyList<CrmProspectingLegalEntityMatch> discovered)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        ArgumentNullException.ThrowIfNull(discovered);
        var steps = new List<CrmProspectingIdentityBootstrapStep>();
        var normalizedName = Normalize(companyName);
        steps.Add(new CrmProspectingIdentityBootstrapStep("seed", "seed_normalized", normalizedName));

        var candidates = discovered
            .Where(item => item is not null)
            .GroupBy(item => item.Lei, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Lei, StringComparer.Ordinal)
            .ToArray();
        steps.Add(new CrmProspectingIdentityBootstrapStep("discover", "candidate_count", candidates.Length.ToString()));

        if (candidates.Length == 0)
            return new CrmProspectingIdentityBootstrapResult(
                CrmProspectingIdentityBootstrapStage.Unresolved, null, [], steps);

        var exact = candidates
            .Where(item => string.Equals(Normalize(item.LegalName), normalizedName, StringComparison.Ordinal))
            .ToArray();
        steps.Add(new CrmProspectingIdentityBootstrapStep("name", "exact_name_matches", exact.Length.ToString()));
        if (exact.Length == 0)
            return new CrmProspectingIdentityBootstrapResult(
                CrmProspectingIdentityBootstrapStage.NeedsOperatorReconciliation, null, candidates, steps);

        // Country, when supplied, is a compatibility check, never a fuzzy signal on its own.
        var countryCompatible = string.IsNullOrWhiteSpace(countryCode)
            ? exact
            : exact.Where(item => string.Equals(item.CountryCode, countryCode.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(item.CountryCode)).ToArray();
        steps.Add(new CrmProspectingIdentityBootstrapStep("country", "country_compatible_matches", countryCompatible.Length.ToString()));

        if (countryCompatible.Length == 1)
        {
            steps.Add(new CrmProspectingIdentityBootstrapStep("promote", "exact_registry_match", countryCompatible[0].Lei));
            return new CrmProspectingIdentityBootstrapResult(
                CrmProspectingIdentityBootstrapStage.ExactRegistryMatch, countryCompatible[0], candidates, steps);
        }

        steps.Add(new CrmProspectingIdentityBootstrapStep("stop", "ambiguous_candidates", "operator reconciliation required"));
        return new CrmProspectingIdentityBootstrapResult(
            CrmProspectingIdentityBootstrapStage.NeedsOperatorReconciliation, null, candidates, steps);
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
