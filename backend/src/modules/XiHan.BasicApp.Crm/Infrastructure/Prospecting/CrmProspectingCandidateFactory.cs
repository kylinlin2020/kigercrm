using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Shared, deterministic Candidate field mapping for synchronous Runs and asynchronous result pages.
/// Duplicate classification is supplied by the caller because it requires tenant-scoped repository reads.
/// </summary>
public static class CrmProspectingCandidateFactory
{
    public static string? NormalizeEmail(CrmProspectingRawRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var email = string.IsNullOrWhiteSpace(record.BusinessEmail) ? null : record.BusinessEmail.Trim();
        if (email?.Length > 254) email = null;
        return CrmLeadDuplicateMatcher.NormalizeEmail(email);
    }

    public static string? NormalizeWebsiteHost(CrmProspectingRawRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var website = string.IsNullOrWhiteSpace(record.Website) ? null : record.Website.Trim();
        if (website?.Length > 500) website = website[..500];
        return CrmLeadDuplicateMatcher.NormalizeDomain(website);
    }

    public static SysCrmProspectingCandidate Create(
        long runId,
        string providerCode,
        CrmProspectingRawRecord record,
        string externalId,
        string companyName,
        string? normalizedEmail,
        string? websiteHost,
        bool isDuplicate,
        long? duplicateLeadId,
        string? duplicateReason)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        if (string.IsNullOrWhiteSpace(providerCode)) throw new ArgumentException("Provider code is required.", nameof(providerCode));
        if (string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("External identity is required.", nameof(externalId));
        if (string.IsNullOrWhiteSpace(companyName)) throw new ArgumentException("Company name is required.", nameof(companyName));

        var email = string.IsNullOrWhiteSpace(record.BusinessEmail) ? null : record.BusinessEmail.Trim();
        if (email?.Length > 254) email = null;
        var website = string.IsNullOrWhiteSpace(record.Website) ? null : record.Website.Trim();
        if (website?.Length > 500) website = website[..500];
        var source = record.Source?.Trim();
        var title = record.Title?.Trim();
        var contactName = record.ContactName?.Trim();
        var summary = record.Summary?.Trim();

        return new SysCrmProspectingCandidate
        {
            RunId = runId,
            ProviderCode = providerCode.Trim(),
            ExternalId = externalId,
            Source = string.IsNullOrWhiteSpace(source) ? null : source[..Math.Min(source.Length, 200)],
            Title = string.IsNullOrWhiteSpace(title) ? null : title[..Math.Min(title.Length, 300)],
            CompanyName = companyName.Trim()[..Math.Min(companyName.Trim().Length, 200)],
            ContactName = string.IsNullOrWhiteSpace(contactName) ? null : contactName[..Math.Min(contactName.Length, 200)],
            BusinessEmail = email,
            NormalizedEmail = normalizedEmail,
            Website = website,
            WebsiteHost = websiteHost,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary[..Math.Min(summary.Length, 5000)],
            Status = isDuplicate ? CrmProspectingCandidateStatus.Duplicate : CrmProspectingCandidateStatus.PendingReview,
            DuplicateLeadId = duplicateLeadId,
            DuplicateReason = duplicateReason
        };
    }
}
