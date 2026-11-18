// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.Mappers;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Qualification four-step application boundary. Every result is review-only: no candidate field is
/// overwritten and no Lead is created. The identity resolver is invoked only by its explicit command
/// so report generation stays deterministic and offline.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingQualificationAppService(
    ICrmProspectingCandidateRepository candidates,
    ICrmProspectingEvidenceRepository evidence,
    ICrmProspectingVerificationReportRepository reports,
    ICrmProspectingRunRepository runs,
    ICrmProspectingCampaignRepository campaigns,
    ICrmProspectingIdentityResolver identityResolver,
    ICrmProspectingPageFetcher pageFetcher,
    IUserDataScopeFilterService dataScope)
    : CrmApplicationService, ICrmProspectingQualificationAppService
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonSerializerDefaults.Web);

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingIdentityMatchDto?> ResolveIdentityAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        var match = await identityResolver.ResolveExactAsync(companyName, cancellationToken);
        return match is null ? null : CrmProspectingMapper.ToIdentityMatchDto(match);
    }

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public Task<CrmProspectingWebsiteAssessmentDto> AssessWebsiteAsync(
        CrmProspectingWebsiteAssessmentInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(typeof(CrmProspectingWebsiteSourceKind), input.SourceKind))
            throw new ArgumentOutOfRangeException(nameof(input.SourceKind));
        var assessment = CrmProspectingWebsiteDiscoveryPolicy.Assess(new CrmProspectingWebsiteCandidateInput(
            input.CompanyName,
            input.CandidateUrl,
            input.SourceProviderCode,
            (CrmProspectingWebsiteSourceKind)input.SourceKind,
            input.PageTitle,
            input.RegistryLegalName));
        return Task.FromResult(CrmProspectingMapper.ToWebsiteAssessmentDto(assessment));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingVerificationReportDto> BuildVerificationReportAsync(
        CrmProspectingVerificationBuildDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidate = await EnsureAccessibleAsync(input.CandidateId, cancellationToken);
        var evidenceRows = await evidence.GetListAsync(item => item.CandidateId == candidate.BasicId, cancellationToken);
        var identityMatch = input.IdentityMatch is null
            ? null
            : CrmProspectingMapper.FromIdentityMatchDto(input.IdentityMatch);
        var data = CrmProspectingVerificationPolicy.Build(candidate, evidenceRows, identityMatch);

        var entity = new SysCrmProspectingVerificationReport
        {
            CandidateId = candidate.BasicId,
            Verdict = (int)data.Verdict,
            IdentityAuthority = data.IdentityAuthority,
            SnapshotJson = JsonSerializer.Serialize(data, ReportJsonOptions)
        };
        return CrmProspectingMapper.ToVerificationReportDto(
            await reports.AddAsync(entity, cancellationToken));
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmProspectingVerificationReportDto?> GetVerificationReportAsync(
        long candidateId,
        CancellationToken cancellationToken = default)
    {
        await EnsureAccessibleAsync(candidateId, cancellationToken);
        var report = await reports.GetLatestByCandidateAsync(candidateId, cancellationToken);
        return report is null ? null : CrmProspectingMapper.ToVerificationReportDto(report);
    }

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingIdentityBootstrapDto> BootstrapIdentityAsync(
        string companyName,
        string? countryCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyName);
        var discovered = await identityResolver.DiscoverAsync(companyName, cancellationToken);
        return CrmProspectingMapper.ToIdentityBootstrapDto(
            CrmProspectingIdentityBootstrapPolicy.Build(companyName, countryCode, discovered));
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmProspectingPageFetchDto> ProbeWebsiteAsync(
        CrmProspectingWebsiteProbeInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CandidateId > 0)
            await EnsureAccessibleAsync(input.CandidateId, cancellationToken);

        var result = await pageFetcher.FetchAsync(input.CandidateUrl, cancellationToken);
        var dto = CrmProspectingMapper.ToPageFetchDto(result);
        if (!result.Succeeded || input.CandidateId <= 0) return dto;

        // Only bounded, policy-accepted fetches become evidence. Identical URL + body hash is a
        // no-op replay instead of a duplicate evidence row.
        var urlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.FinalUrl))).ToLowerInvariant();
        var existing = await evidence.GetListAsync(item =>
            item.CandidateId == input.CandidateId
            && item.SourceUrlHash == urlHash
            && item.RawPayloadHash == result.BodyHash, cancellationToken);
        if (existing.Count == 0)
        {
            await evidence.AddAsync(new SysCrmProspectingEvidence
            {
                CandidateId = input.CandidateId,
                ProviderCode = "website-probe",
                ProviderRecordId = input.CandidateId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FieldName = "website_probe",
                FieldValueHash = result.BodyHash,
                SourceUrl = result.FinalUrl,
                SourceUrlHash = urlHash,
                EvidenceType = "website_probe",
                VerificationStatus = "unverified",
                EvidenceSummary = $"status={result.StatusCode}; bytes={result.ByteCount}; title={result.Title ?? string.Empty}",
                RetrievedAtUtc = DateTimeOffset.UtcNow,
                RawPayloadHash = result.BodyHash
            }, cancellationToken);
        }
        dto.EvidencePersisted = true;
        return dto;
    }

    private async Task<SysCrmProspectingCandidate> EnsureAccessibleAsync(long candidateId, CancellationToken cancellationToken)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        var candidate = await candidates.GetByIdAsync(candidateId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting candidate does not exist.");
        var run = await runs.GetByIdAsync(candidate.RunId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting run does not exist.");
        var campaign = await campaigns.GetByIdAsync(run.CampaignId, cancellationToken)
            ?? throw new InvalidOperationException("Prospecting campaign does not exist.");
        await CrmAssignmentAuthorization.EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId ?? 0, cancellationToken);
        return candidate;
    }
}
