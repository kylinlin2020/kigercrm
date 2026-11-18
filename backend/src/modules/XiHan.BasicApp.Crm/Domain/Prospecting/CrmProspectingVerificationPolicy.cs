using XiHan.BasicApp.Crm.Domain.Entities;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Field-level verification strength. Verified means an authoritative source was supplied.</summary>
public enum CrmProspectingVerificationState
{
    Verified = 0,
    Unverified = 1,
    Insufficient = 2,
    Conflict = 3,
}

/// <summary>Overall qualification outcome. ReadyForReview still requires a human decision.</summary>
public enum CrmProspectingQualificationVerdict
{
    ReadyForReview = 0,
    NeedsReview = 1,
    Blocked = 2,
}

public sealed record CrmProspectingFieldVerification(
    string Field,
    CrmProspectingVerificationState State,
    string Authority,
    string ReasonCode);

/// <summary>
/// Frozen, explainable qualification result. It never mutates the candidate: every field remains
/// subject to human review before it can become authoritative CRM data.
/// </summary>
public sealed record CrmProspectingVerificationReportData(
    long CandidateId,
    CrmProspectingQualificationVerdict Verdict,
    string IdentityAuthority,
    IReadOnlyList<CrmProspectingFieldVerification> Fields,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> RecommendedActions,
    string Summary);

/// <summary>
/// Deterministic qualification policy combining four bounded steps: staged identity bootstrap,
/// website candidate assessment, contact corroboration and a terminal/no-result report.
/// It performs no network calls and never writes to the candidate.
/// </summary>
public static class CrmProspectingVerificationPolicy
{
    public static CrmProspectingVerificationReportData Build(
        SysCrmProspectingCandidate candidate,
        IReadOnlyList<SysCrmProspectingEvidence> evidence,
        CrmProspectingLegalEntityMatch? identityMatch)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(evidence);

        var fields = new List<CrmProspectingFieldVerification>();
        var blockers = new List<string>();
        var missing = new List<string>();
        var actions = new List<string>();

        // 1) Staged identity bootstrap: only an exact LEI match is authoritative.
        if (identityMatch is not null
            && string.Equals(Normalize(identityMatch.LegalName), Normalize(candidate.CompanyName), StringComparison.Ordinal))
        {
            fields.Add(new CrmProspectingFieldVerification(
                "legalName", CrmProspectingVerificationState.Verified, "GLEIF:" + identityMatch.Lei, "registry_exact_match"));
        }
        else if (identityMatch is not null)
        {
            fields.Add(new CrmProspectingFieldVerification(
                "legalName", CrmProspectingVerificationState.Conflict, "GLEIF", "registry_name_mismatch"));
            blockers.Add("The supplied LEI match does not match the candidate's company name.");
            actions.Add("Ask an operator to reconcile the candidate name with the LEI legal name.");
        }
        else
        {
            fields.Add(new CrmProspectingFieldVerification(
                "legalName", CrmProspectingVerificationState.Unverified, "none", "no_authoritative_match"));
            missing.Add("legalName");
            actions.Add("Run an authoritative identity lookup before approving this candidate.");
        }

        // 2) Website probe assessment reuses the existing review-only policy; no fetch is performed.
        if (string.IsNullOrWhiteSpace(candidate.Website))
        {
            fields.Add(new CrmProspectingFieldVerification(
                "website", CrmProspectingVerificationState.Insufficient, "none", "no_candidate_url"));
            missing.Add("website");
            actions.Add("Supply a candidate website URL for review-only assessment.");
        }
        else
        {
            var assessment = CrmProspectingWebsiteDiscoveryPolicy.Assess(new CrmProspectingWebsiteCandidateInput(
                candidate.CompanyName,
                candidate.Website,
                "manual-review",
                CrmProspectingWebsiteSourceKind.ManualReview));
            fields.Add(new CrmProspectingFieldVerification(
                "website",
                assessment.AcceptedForReview
                    ? CrmProspectingVerificationState.Unverified
                    : CrmProspectingVerificationState.Conflict,
                assessment.NormalizedUrl,
                assessment.ReasonCode));
            if (!assessment.AcceptedForReview)
            {
                blockers.Add("The candidate website failed the public HTTPS review policy.");
                actions.Add("Reject the unsafe website candidate and request a new one.");
            }
        }

        // 3) Contact enrichment: evidence supplies provenance and corroboration only, never values.
        Corroborate(candidate.BusinessEmail, "businessEmail", "email", evidence, fields, missing, actions);
        Corroborate(candidate.ContactName, "contactName", "contact", evidence, fields, missing, actions);
        var countryEvidence = evidence.Where(item =>
                item.FieldName.Contains("country", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        fields.Add(new CrmProspectingFieldVerification(
            "country",
            countryEvidence.Length > 0 ? CrmProspectingVerificationState.Verified : CrmProspectingVerificationState.Insufficient,
            countryEvidence.Length > 0 ? countryEvidence[0].ProviderCode : "none",
            countryEvidence.Length > 0 ? "evidence_corroborated" : "no_corroborating_evidence"));
        if (countryEvidence.Length == 0) missing.Add("country");

        // 4) Terminal / no-result report: explicit blockers, gaps and next actions.
        var conflicts = fields.Count(field => field.State == CrmProspectingVerificationState.Conflict);
        var verdict = conflicts > 0
            ? CrmProspectingQualificationVerdict.Blocked
            : fields.All(field => field.State == CrmProspectingVerificationState.Verified)
                ? CrmProspectingQualificationVerdict.ReadyForReview
                : CrmProspectingQualificationVerdict.NeedsReview;
        if (verdict == CrmProspectingQualificationVerdict.NeedsReview)
            actions.Add("Review the listed gaps manually; do not convert this candidate yet.");

        var authority = identityMatch is null ? "none" : "GLEIF:" + identityMatch.Lei;
        var summary = verdict switch
        {
            CrmProspectingQualificationVerdict.Blocked => $"Qualification blocked by {conflicts} conflicting field(s).",
            CrmProspectingQualificationVerdict.ReadyForReview => "All fields verified; human review is still required.",
            _ => $"Qualification incomplete: {missing.Count} field(s) lack corroboration.",
        };
        return new CrmProspectingVerificationReportData(
            candidate.BasicId, verdict, authority, fields, blockers, missing, actions, summary);
    }

    private static void Corroborate(
        string? candidateValue,
        string field,
        string evidenceToken,
        IReadOnlyList<SysCrmProspectingEvidence> evidence,
        List<CrmProspectingFieldVerification> fields,
        List<string> missing,
        List<string> actions)
    {
        if (string.IsNullOrWhiteSpace(candidateValue))
        {
            fields.Add(new CrmProspectingFieldVerification(field, CrmProspectingVerificationState.Insufficient, "none", "candidate_value_missing"));
            missing.Add(field);
            actions.Add($"Supply {field} before qualification can complete.");
            return;
        }

        var supporting = evidence.Where(item =>
                item.FieldName.Contains(evidenceToken, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.RetrievedAtUtc)
            .ToArray();
        if (supporting.Length == 0)
        {
            fields.Add(new CrmProspectingFieldVerification(field, CrmProspectingVerificationState.Unverified, "none", "no_corroborating_evidence"));
            missing.Add(field);
            actions.Add($"Find corroborating evidence for {field}.");
            return;
        }
        fields.Add(new CrmProspectingFieldVerification(
            field,
            CrmProspectingVerificationState.Verified,
            supporting[0].ProviderCode,
            "evidence_corroborated"));
    }

    private static string Normalize(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
