using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Domain.Research;
using XiHan.BasicApp.Crm.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Security.Users;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Builds a customer research dossier from real local CRM records only. Regeneration supersedes
/// the previous dossier; market opportunity review is an explicit, permission-guarded action.
/// No network client, AI provider or silent fallback is involved.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "BackgroundResearch")]
public sealed class CrmBackgroundResearchAppService(
    ICrmBackgroundResearchRepository repository,
    ICrmCustomerRepository customers,
    ICrmOpportunityRepository opportunities,
    ICrmLeadRepository leads,
    ICurrentUser currentUser)
    : CrmApplicationService, ICrmBackgroundResearchAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmBackgroundResearchDetailDto> GenerateAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer does not exist.");

        var customerOpportunities = (await opportunities.GetAllAsync(cancellationToken))
            .Where(item => item.CustomerId == customerId)
            .ToList();
        var sourceLead = customer.ConvertedLeadId is { } leadId
            ? await leads.GetByIdAsync(leadId, cancellationToken)
            : null;
        var aggregation = CrmBackgroundResearchAggregator.Aggregate(
            customer,
            customerOpportunities,
            sourceLead is null ? [] : [sourceLead]);

        var now = DateTimeOffset.UtcNow;
        await repository.SupersedeAsync(customerId, cancellationToken);
        var report = await repository.AddReportAsync(new SysCrmBackgroundResearchReport
        {
            CustomerId = customerId,
            Status = CrmResearchReportStatus.Current,
            GeneratedAtUtc = now,
            GeneratedByUserId = currentUser.UserId,
            EvidenceCount = aggregation.Evidence.Count,
            OpportunityCount = aggregation.Opportunities.Count
        }, cancellationToken);

        await repository.AddEvidenceAsync(
            aggregation.Evidence.Select(fact => new SysCrmResearchEvidence
            {
                ReportId = report.BasicId,
                Source = fact.Source,
                FactKey = fact.FactKey,
                FactValue = fact.FactValue,
                SourceRef = fact.SourceRef,
                Confidence = fact.Confidence
            }),
            cancellationToken);
        await repository.AddOpportunitiesAsync(
            aggregation.Opportunities.Select(item => new SysCrmMarketOpportunity
            {
                ReportId = report.BasicId,
                CustomerId = customerId,
                Title = item.Title,
                FactKey = item.FactKey,
                FactValue = item.FactValue,
                PriorityScore = item.PriorityScore,
                Status = CrmMarketOpportunityStatus.Proposed
            }),
            cancellationToken);
        return await ToDetailAsync(report, customer.Name, cancellationToken);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmBackgroundResearchDetailDto?> GetCurrentAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var report = await repository.GetCurrentByCustomerAsync(customerId, cancellationToken);
        if (report is null) return null;
        var customer = await customers.GetByIdAsync(customerId, cancellationToken);
        return await ToDetailAsync(report, customer?.Name ?? string.Empty, cancellationToken);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmBackgroundResearchReportDto>> GetHistoryAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var reports = await repository.GetByCustomerAsync(customerId, cancellationToken);
        var customer = await customers.GetByIdAsync(customerId, cancellationToken);
        return reports.Select(report => new CrmBackgroundResearchReportDto
        {
            BasicId = report.BasicId,
            CustomerId = report.CustomerId,
            CustomerName = customer?.Name ?? string.Empty,
            Status = (int)report.Status,
            GeneratedAtUtc = report.GeneratedAtUtc,
            GeneratedByUserId = report.GeneratedByUserId,
            EvidenceCount = report.EvidenceCount,
            OpportunityCount = report.OpportunityCount
        }).ToList();
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmMarketOpportunityDto> ReviewOpportunityAsync(
        CrmMarketOpportunityReviewDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Status != (int)CrmMarketOpportunityStatus.Confirmed
            && input.Status != (int)CrmMarketOpportunityStatus.Dismissed)
            throw new ArgumentOutOfRangeException(nameof(input), "Only confirmed or dismissed is a review decision.");
        var target = (CrmMarketOpportunityStatus)input.Status;
        if (!await repository.SetOpportunityStatusAsync(
                input.OpportunityId, CrmMarketOpportunityStatus.Proposed, target, cancellationToken))
            throw new InvalidOperationException("Opportunity is not pending review or does not exist.");
        var opportunity = await repository.GetOpportunityAsync(input.OpportunityId, cancellationToken)
            ?? throw new InvalidOperationException("Opportunity does not exist.");
        return new CrmMarketOpportunityDto
        {
            BasicId = opportunity.BasicId,
            ReportId = opportunity.ReportId,
            CustomerId = opportunity.CustomerId,
            Title = opportunity.Title,
            FactKey = opportunity.FactKey,
            FactValue = opportunity.FactValue,
            PriorityScore = opportunity.PriorityScore,
            Status = (int)opportunity.Status
        };
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmBackgroundResearchExportDto> GetExportAsync(
        long customerId,
        string format,
        CancellationToken cancellationToken = default)
    {
        var normalized = CrmBackgroundResearchExporter.NormalizeFormat(format);
        var detail = await GetCurrentAsync(customerId, cancellationToken)
            ?? throw new InvalidOperationException("No current research report to export.");
        var now = DateTimeOffset.UtcNow;
        return new CrmBackgroundResearchExportDto
        {
            FileName = CrmBackgroundResearchExporter.BuildFileName(customerId, normalized, now),
            ContentType = normalized == CrmBackgroundResearchExporter.Json
                ? "application/json"
                : "text/markdown",
            Content = normalized == CrmBackgroundResearchExporter.Json
                ? CrmBackgroundResearchExporter.RenderJson(detail)
                : CrmBackgroundResearchExporter.RenderMarkdown(detail)
        };
    }

    private async Task<CrmBackgroundResearchDetailDto> ToDetailAsync(
        SysCrmBackgroundResearchReport report,
        string customerName,
        CancellationToken cancellationToken)
    {
        var evidence = await repository.GetEvidenceAsync(report.BasicId, cancellationToken);
        var opportunities = await repository.GetOpportunitiesAsync(report.BasicId, cancellationToken);
        return new CrmBackgroundResearchDetailDto
        {
            Report = new CrmBackgroundResearchReportDto
            {
                BasicId = report.BasicId,
                CustomerId = report.CustomerId,
                CustomerName = customerName,
                Status = (int)report.Status,
                GeneratedAtUtc = report.GeneratedAtUtc,
                GeneratedByUserId = report.GeneratedByUserId,
                EvidenceCount = report.EvidenceCount,
                OpportunityCount = report.OpportunityCount
            },
            Evidence = evidence.Select(item => new CrmResearchEvidenceDto
            {
                BasicId = item.BasicId,
                ReportId = item.ReportId,
                Source = (int)item.Source,
                FactKey = item.FactKey,
                FactValue = item.FactValue,
                SourceRef = item.SourceRef,
                Confidence = item.Confidence
            }).ToList(),
            Opportunities = opportunities.Select(item => new CrmMarketOpportunityDto
            {
                BasicId = item.BasicId,
                ReportId = item.ReportId,
                CustomerId = item.CustomerId,
                Title = item.Title,
                FactKey = item.FactKey,
                FactValue = item.FactValue,
                PriorityScore = item.PriorityScore,
                Status = (int)item.Status
            }).ToList()
        };
    }
}
