using Microsoft.AspNetCore.Mvc;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Domain.Services;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Extensions;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
using XiHan.Framework.Domain.Shared.Paging.Enums;
using XiHan.Framework.Domain.Shared.Paging.Models;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

public interface ICrmLeadDuplicateQueryService : IApplicationService
{
    Task<IReadOnlyList<CrmLeadDuplicateCandidateDto>> FindPotentialDuplicatesAsync(
        CrmLeadDuplicateCheckDto input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Finds bounded, tenant- and owner-scoped lead candidates for the duplicate scoring engine.
/// Candidate preselection is deliberately conservative and does not enforce uniqueness.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Leads")]
public sealed class CrmLeadDuplicateQueryService(
    ICrmLeadRepository repository,
    IFieldSecurityService fieldSecurity,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmLeadDuplicateQueryService
{
    private const int CandidatePageSize = 100;
    private const int MaximumReturnedCandidates = 50;

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    [HttpPost]
    public async Task<IReadOnlyList<CrmLeadDuplicateCandidateDto>> FindPotentialDuplicatesAsync(
        CrmLeadDuplicateCheckDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        if (input.ExcludeLeadId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(input.ExcludeLeadId));
        ValidateLength(input.CompanyName, 200, nameof(input.CompanyName));
        ValidateLength(input.Email, 254, nameof(input.Email));
        ValidateLength(input.Phone, 40, nameof(input.Phone));
        ValidateLength(input.Website, 500, nameof(input.Website));

        var company = NormalizeInput(input.CompanyName);
        var email = CrmLeadDuplicateMatcher.NormalizeEmail(input.Email);
        var phone = CrmLeadDuplicateMatcher.NormalizePhone(input.Phone);
        var websiteHost = CrmLeadDuplicateMatcher.NormalizeDomain(input.Website);
        if (company is null && email is null && phone is null && websiteHost is null)
            throw new ArgumentException("At least one lead identity signal is required.", nameof(input));

        var source = new SysCrmLead
        {
            BasicId = input.ExcludeLeadId ?? 0,
            CompanyName = company ?? string.Empty,
            Email = email,
            Phone = input.Phone,
            Website = input.Website
        };

        var candidates = new Dictionary<long, SysCrmLead>();

        async Task LoadAsync(Action<QueryConditions> addSignal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new BasicAppPRDto
            {
                Page = new PageRequestMetadata(1, CandidatePageSize),
                Conditions = new QueryConditions()
            };
            addSignal(request.Conditions);
            await fieldSecurity.GuardQueryAsync(request.Conditions, typeof(SysCrmLead), cancellationToken);
            await request.Conditions.ApplyOwnerScopeAsync(
                dataScope, (SysCrmLead lead) => lead.OwnerUserId, DateTimeOffset.UtcNow, cancellationToken);
            request.Conditions.AddSort((SysCrmLead lead) => lead.CreatedTime, SortDirection.Descending, 0);

            var page = await repository.GetPagedAsync(request, cancellationToken);
            foreach (var candidate in page.Items)
            {
                if (candidate.BasicId != source.BasicId)
                    candidates.TryAdd(candidate.BasicId, candidate);
            }
        }

        if (email is not null)
            await LoadAsync(conditions => conditions.AddFilter((SysCrmLead lead) => lead.DuplicateEmailNormalized, email));
        if (phone is not null)
            await LoadAsync(conditions => conditions.AddFilter((SysCrmLead lead) => lead.PhoneDigits, phone));
        if (websiteHost is not null)
            await LoadAsync(conditions => conditions.AddFilter((SysCrmLead lead) => lead.WebsiteHost, websiteHost));
        if (company is not null)
            await LoadAsync(conditions => conditions.SetKeyword<SysCrmLead>(company, lead => lead.CompanyName));

        return CrmLeadDuplicateMatcher.FindMatches(source, candidates.Values, 50)
            .Take(MaximumReturnedCandidates)
            .Select(match => new CrmLeadDuplicateCandidateDto
            {
                Candidate = CrmLeadMapper.ToListItemDto(match.Candidate),
                Score = match.Score,
                Confidence = match.Confidence.ToString(),
                MatchedSignals = match.MatchedSignals
            })
            .ToList();
    }

    private static string? NormalizeInput(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateLength(string? value, int maximum, string parameterName)
    {
        if (value is not null && value.Length > maximum)
            throw new ArgumentOutOfRangeException(parameterName, $"Maximum length is {maximum}.");
    }
}
