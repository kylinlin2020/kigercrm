using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Research;

public sealed record CrmResearchEvidenceDraft(
    CrmResearchSource Source,
    string FactKey,
    string FactValue,
    string? SourceRef,
    int Confidence);

public sealed record CrmMarketOpportunityDraft(
    string Title,
    string FactKey,
    string FactValue,
    int PriorityScore);

public sealed record CrmResearchAggregation(
    IReadOnlyList<CrmResearchEvidenceDraft> Evidence,
    IReadOnlyList<CrmMarketOpportunityDraft> Opportunities);

/// <summary>
/// Builds a research dossier from three local CRM sources: customer profile, the customer's
/// opportunities, and the lead it was converted from. Pure function, so it is deterministic and
/// fully testable without databases, networks or AI providers.
/// </summary>
public static class CrmBackgroundResearchAggregator
{
    public const string FactLegalName = "LegalName";
    public const string FactIndustry = "Industry";
    public const string FactCountryRegion = "CountryRegion";
    public const string FactWebsite = "Website";
    public const string FactSource = "Source";
    public const string FactNotes = "Notes";
    public const string FactOpenOpportunity = "OpenOpportunity";
    public const string FactSourceLead = "SourceLead";

    public static CrmResearchAggregation Aggregate(
        SysCrmCustomer customer,
        IEnumerable<SysCrmOpportunity> opportunities,
        IEnumerable<SysCrmLead> leads)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(opportunities);
        ArgumentNullException.ThrowIfNull(leads);

        var evidence = new List<CrmResearchEvidenceDraft>();
        var profileRef = $"Crm_Customer:{customer.BasicId}";
        Add(evidence, CrmResearchSource.CustomerProfile, FactLegalName, customer.LegalName, profileRef, 95);
        Add(evidence, CrmResearchSource.CustomerProfile, FactIndustry, customer.Industry, profileRef, 90);
        Add(evidence, CrmResearchSource.CustomerProfile, FactCountryRegion, customer.CountryRegion, profileRef, 90);
        Add(evidence, CrmResearchSource.CustomerProfile, FactWebsite, customer.Website, profileRef, 80);
        Add(evidence, CrmResearchSource.CustomerProfile, FactSource, customer.Source, profileRef, 70);
        Add(evidence, CrmResearchSource.CustomerProfile, FactNotes, customer.Notes, profileRef, 50);

        var opportunityDrafts = new List<CrmMarketOpportunityDraft>();
        foreach (var opportunity in opportunities)
        {
            var reference = $"Crm_Opportunity:{opportunity.BasicId}";
            var amount = $"{opportunity.Amount:0.##} {opportunity.Currency}";
            Add(evidence, CrmResearchSource.OpenOpportunity, FactOpenOpportunity,
                $"{opportunity.Name} | {opportunity.Stage} | {amount}", reference, 85);

            // Won and Lost are closed; they are evidence but no longer a market opportunity.
            if (opportunity.Stage is CrmOpportunityStage.Won or CrmOpportunityStage.Lost) continue;
            opportunityDrafts.Add(new CrmMarketOpportunityDraft(
                CrmBackgroundResearchPolicy.Truncate(opportunity.Name),
                FactOpenOpportunity,
                CrmBackgroundResearchPolicy.Truncate($"{opportunity.Stage} | {amount} | {opportunity.Probability ?? 0}%"),
                CrmBackgroundResearchPolicy.OpportunityPriority(opportunity.Amount, opportunity.Probability)));
        }

        if (customer.ConvertedLeadId is { } leadId)
        {
            var lead = leads.FirstOrDefault(item => item.BasicId == leadId);
            if (lead is not null)
            {
                Add(evidence, CrmResearchSource.SourceLead, FactSourceLead,
                    $"{lead.CompanyName} | {lead.ContactName}", $"Crm_Lead:{lead.BasicId}", 80);
            }
        }

        var selectedEvidence = evidence.Where(CrmBackgroundResearchPolicy.IsIncluded)
            .Take(CrmBackgroundResearchPolicy.MaxEvidence).ToList();
        var selectedOpportunities = opportunityDrafts
            .OrderByDescending(item => item.PriorityScore)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .Take(CrmBackgroundResearchPolicy.MaxOpportunities).ToList();
        return new CrmResearchAggregation(selectedEvidence, selectedOpportunities);
    }

    private static void Add(
        List<CrmResearchEvidenceDraft> target,
        CrmResearchSource source,
        string factKey,
        string? value,
        string? sourceRef,
        int confidence)
    {
        var text = CrmBackgroundResearchPolicy.Truncate(value);
        if (string.IsNullOrWhiteSpace(text)) return;
        target.Add(new CrmResearchEvidenceDraft(
            source, factKey, text, sourceRef, CrmBackgroundResearchPolicy.ClampConfidence(confidence)));
    }
}
