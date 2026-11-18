// Copyright (c) 2026 KIGER contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Application.Mappers;

/// <summary>
/// Prospecting entity-to-DTO mappings used by application services and field-level security discovery.
/// </summary>
public static class CrmProspectingMapper
{
    private static readonly JsonSerializerOptions SuperSearchJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps a prospecting candidate entity to its review-list DTO.</summary>
    public static CrmProspectingCandidateDto ToCandidateDto(SysCrmProspectingCandidate entity) => new()
    {
        BasicId = entity.BasicId,
        RunId = entity.RunId,
        ProviderCode = entity.ProviderCode,
        ExternalId = entity.ExternalId,
        Source = entity.Source,
        Title = entity.Title,
        CompanyName = entity.CompanyName,
        ContactName = entity.ContactName,
        BusinessEmail = entity.BusinessEmail,
        Website = entity.Website,
        Summary = entity.Summary,
        Status = (int)entity.Status,
        DuplicateReason = entity.DuplicateReason,
        ConvertedLeadId = entity.ConvertedLeadId,
        ShortlistedAt = entity.ShortlistedAt,
        CreatedTime = entity.CreatedTime
    };

    /// <summary>Maps a campaign to its DTO so the response filter can recognise the registered entity.</summary>
    public static CrmProspectingCampaignDto ToCampaignDto(SysCrmProspectingCampaign entity) => new()
    {
        BasicId = entity.BasicId,
        Name = entity.Name,
        Objective = entity.Objective,
        ProductScope = entity.ProductScope,
        TargetMarkets = entity.TargetMarkets,
        IdealCustomerProfile = entity.IdealCustomerProfile,
        Status = (int)entity.Status,
        OwnerUserId = entity.OwnerUserId,
        ActiveStrategyVersion = entity.ActiveStrategyVersion,
        CreatedTime = entity.CreatedTime
    };

    /// <summary>Maps a strategy version to its DTO.</summary>
    public static CrmProspectingStrategyDto ToStrategyDto(SysCrmProspectingStrategy entity) => new()
    {
        BasicId = entity.BasicId,
        CampaignId = entity.CampaignId,
        Version = entity.Version,
        Name = entity.Name,
        SearchObjective = entity.SearchObjective,
        ProviderPlanJson = entity.ProviderPlanJson,
        ExcludedDomainsJson = entity.ExcludedDomainsJson,
        ApprovalStatus = (int)entity.ApprovalStatus,
        ApprovedBy = entity.ApprovedBy,
        ApprovedAt = entity.ApprovedAt,
        ActivatedAt = entity.ActivatedAt
    };

    /// <summary>Maps a Run to its DTO.</summary>
    public static CrmProspectingRunDto ToRunDto(SysCrmProspectingRun entity) => new()
    {
        BasicId = entity.BasicId,
        CampaignId = entity.CampaignId,
        StrategyId = entity.StrategyId,
        StrategyVersion = entity.StrategyVersion,
        Status = (int)entity.Status,
        CreatedTime = entity.CreatedTime,
        StartedAt = entity.StartedAt,
        CompletedAt = entity.CompletedAt,
        RawRecordCount = entity.RawRecordCount,
        CandidateCount = entity.CandidateCount,
        FailureCount = entity.FailureCount,
        FailureSummary = entity.FailureSummary
    };

    /// <summary>Maps a provenance evidence row to its DTO.</summary>
    public static CrmProspectingEvidenceDto ToEvidenceDto(SysCrmProspectingEvidence entity) => new()
    {
        BasicId = entity.BasicId,
        ProviderCode = entity.ProviderCode,
        ProviderRecordId = entity.ProviderRecordId,
        FieldName = entity.FieldName,
        FieldValueHash = entity.FieldValueHash,
        SourceUrl = entity.SourceUrl,
        EvidenceType = entity.EvidenceType,
        VerificationStatus = entity.VerificationStatus,
        EvidenceSummary = entity.EvidenceSummary,
        PublishedAtUtc = entity.PublishedAtUtc,
        RetrievedAtUtc = entity.RetrievedAtUtc,
        RawPayloadHash = entity.RawPayloadHash
    };

    /// <summary>Maps an immutable score snapshot to its DTO.</summary>
    public static CrmProspectingScoreDto ToScoreDto(SysCrmProspectingScore entity) => new()
    {
        BasicId = entity.BasicId,
        ScorecardVersion = entity.ScorecardVersion,
        Total = entity.Total,
        Priority = entity.Priority,
        IdentityPoints = entity.IdentityPoints,
        ProcurementPoints = entity.ProcurementPoints,
        ProductFitPoints = entity.ProductFitPoints,
        MarketFitPoints = entity.MarketFitPoints,
        RecencyPoints = entity.RecencyPoints,
        EvidencePoints = entity.EvidencePoints,
        ContactPoints = entity.ContactPoints,
        ReviewGatesJson = entity.ReviewGatesJson,
        EvaluatedAtUtc = entity.EvaluatedAtUtc
    };

    /// <summary>Maps a deterministic Super Search plan to its read-only preview DTO.</summary>
    public static CrmProspectingSuperSearchPreviewDto ToSuperSearchPreviewDto(CrmProspectingSuperSearchPlan plan) => new()
    {
        ScopeFingerprint = plan.ScopeFingerprint,
        MaximumRounds = plan.MaximumRounds,
        PlannedRounds = plan.PlannedRounds,
        ProviderCount = plan.ProviderCount,
        TotalQueryCells = plan.TotalQueryCells,
        Rounds = plan.Rounds.Select(ToSuperSearchRoundDto).ToArray()
    };

    private static CrmProspectingSuperSearchRoundDto ToSuperSearchRoundDto(CrmProspectingSuperSearchRoundPlan round) => new()
    {
        RoundNumber = round.RoundNumber,
        ThemeCode = round.ThemeCode,
        ThemeLabel = round.ThemeLabel,
        Market = round.Market,
        CustomerType = round.CustomerType,
        ExcludedTerms = round.ExcludedTerms,
        ExcludedDomains = round.ExcludedDomains,
        QueryCells = round.QueryCells.Select(ToSuperSearchQueryCellDto).ToArray(),
        Fingerprint = round.Fingerprint
    };

    private static CrmProspectingSuperSearchQueryCellDto ToSuperSearchQueryCellDto(CrmProspectingSuperSearchQueryCell cell) => new()
    {
        ProviderCode = cell.ProviderCode,
        Market = cell.Market,
        CustomerType = cell.CustomerType,
        ThemeCode = cell.ThemeCode,
        QueryText = cell.QueryText,
        Fingerprint = cell.Fingerprint
    };

    public static CrmProspectingIdentityBootstrapDto ToIdentityBootstrapDto(CrmProspectingIdentityBootstrapResult result) => new()
    {
        Stage = (int)result.Stage,
        AuthoritativeMatch = result.AuthoritativeMatch is null ? null : ToIdentityMatchDto(result.AuthoritativeMatch),
        Candidates = result.Candidates.Select(ToIdentityMatchDto).ToArray(),
        Steps = result.Steps.Select(step => new CrmProspectingIdentityBootstrapStepDto
        {
            Stage = step.Stage,
            ReasonCode = step.ReasonCode,
            Detail = step.Detail
        }).ToArray()
    };

    public static CrmProspectingPageFetchDto ToPageFetchDto(CrmProspectingPageFetchResult result) => new()
    {
        FinalUrl = result.FinalUrl,
        StatusCode = result.StatusCode,
        Title = result.Title,
        BodyHash = result.BodyHash,
        ByteCount = result.ByteCount,
        ReasonCode = result.ReasonCode,
        Succeeded = result.Succeeded
    };

    /// <summary>Maps an authoritative LEI match to its wire contract.</summary>
    public static CrmProspectingIdentityMatchDto ToIdentityMatchDto(CrmProspectingLegalEntityMatch match) => new()
    {
        Lei = match.Lei,
        LegalName = match.LegalName,
        EntityStatus = match.EntityStatus,
        RegistrationStatus = match.RegistrationStatus,
        CountryCode = match.CountryCode,
        RegisteredAs = match.RegisteredAs,
        SourceUrl = match.SourceUrl,
        RetrievedAtUtc = match.RetrievedAtUtc
    };

    public static CrmProspectingLegalEntityMatch FromIdentityMatchDto(CrmProspectingIdentityMatchDto dto) => new(
        dto.Lei,
        dto.LegalName,
        dto.EntityStatus,
        dto.RegistrationStatus,
        dto.CountryCode,
        dto.RegisteredAs,
        dto.SourceUrl,
        string.Empty,
        dto.RetrievedAtUtc);

    public static CrmProspectingWebsiteAssessmentDto ToWebsiteAssessmentDto(CrmProspectingWebsiteCandidateAssessment assessment) => new()
    {
        AcceptedForReview = assessment.AcceptedForReview,
        CanSetOfficialWebsite = assessment.CanSetOfficialWebsite,
        NormalizedUrl = assessment.NormalizedUrl,
        Host = assessment.Host,
        MatchClass = assessment.MatchClass,
        ReasonCode = assessment.ReasonCode
    };

    /// <summary>Maps a frozen report; a corrupt snapshot fails loudly instead of reporting a fake verdict.</summary>
    public static CrmProspectingVerificationReportDto ToVerificationReportDto(SysCrmProspectingVerificationReport entity)
    {
        var snapshot = JsonSerializer.Deserialize<CrmProspectingVerificationReportData>(entity.SnapshotJson, SuperSearchJsonOptions)
            ?? throw new InvalidOperationException("The persisted verification report snapshot is invalid.");
        return new()
        {
            BasicId = entity.BasicId,
            CandidateId = entity.CandidateId,
            Verdict = (int)snapshot.Verdict,
            IdentityAuthority = snapshot.IdentityAuthority,
            Fields = snapshot.Fields.Select(field => new CrmProspectingFieldVerificationDto
            {
                Field = field.Field,
                State = (int)field.State,
                Authority = field.Authority,
                ReasonCode = field.ReasonCode
            }).ToArray(),
            Blockers = snapshot.Blockers,
            MissingFields = snapshot.MissingFields,
            RecommendedActions = snapshot.RecommendedActions,
            Summary = snapshot.Summary,
            CreatedTime = entity.CreatedTime
        };
    }

    /// <summary>Maps a recurring schedule to its wire contract.</summary>
    public static CrmProspectingScheduleDto ToScheduleDto(SysCrmProspectingSchedule entity) => new()
    {
        BasicId = entity.BasicId,
        CampaignId = entity.CampaignId,
        Name = entity.Name,
        RequestKeyPrefix = entity.RequestKeyPrefix,
        TimeZoneId = entity.TimeZoneId,
        Frequency = (int)entity.Frequency,
        Interval = entity.Interval,
        TimeOfDayMinutes = entity.TimeOfDayMinutes,
        DayOfWeek = entity.DayOfWeek,
        DayOfMonth = entity.DayOfMonth,
        StartDateUtc = entity.StartDateUtc,
        EndDateUtc = entity.EndDateUtc,
        Status = (int)entity.Status,
        NextRunAtUtc = entity.NextRunAtUtc,
        LastRunAtUtc = entity.LastRunAtUtc,
        LastRunId = entity.LastRunId,
        ApprovedBy = entity.ApprovedBy,
        ApprovedAt = entity.ApprovedAt,
        CreatedTime = entity.CreatedTime
    };

    /// <summary>Maps a deterministic objective parse into its wire contract, field by field.</summary>
    public static CrmProspectingSuperSearchObjectiveParseResultDto ToSuperSearchObjectiveParseResultDto(
        CrmProspectingObjectiveParseResult result) => new()
    {
        Input = new CrmProspectingSuperSearchPreviewInputDto
        {
            Products = result.Input.Products,
            Markets = result.Input.Markets,
            CustomerTypes = result.Input.CustomerTypes,
            Industries = result.Input.Industries,
            ProviderCodes = result.Input.ProviderCodes,
            ExcludedTerms = result.Input.ExcludedTerms,
            ExcludedDomains = result.Input.ExcludedDomains,
            Depth = (int)result.Input.Depth,
            RequestedRounds = result.Input.RequestedRounds
        },
        Matches = result.Matches.Select(match => new CrmProspectingObjectiveMatchDto
        {
            Field = match.Field,
            Marker = match.Marker,
            Values = match.Values
        }).ToArray(),
        Warnings = result.Warnings
    };

    /// <summary>Maps a durable mission with its frozen plan; a corrupt snapshot fails loudly.</summary>
    public static CrmProspectingSuperSearchMissionDto ToSuperSearchMissionDto(SysCrmProspectingSuperSearchMission entity)
    {
        var plan = JsonSerializer.Deserialize<CrmProspectingSuperSearchPreviewDto>(entity.PlanSnapshotJson, SuperSearchJsonOptions)
            ?? throw new InvalidOperationException("The persisted Super Search mission plan snapshot is invalid.");
        return new()
        {
            BasicId = entity.BasicId,
            CampaignId = entity.CampaignId,
            Name = entity.Name,
            RequestKey = entity.RequestKey,
            ScopeFingerprint = entity.ScopeFingerprint,
            Depth = entity.Depth,
            PlannedRounds = entity.PlannedRounds,
            ProviderCount = entity.ProviderCount,
            Status = (int)entity.Status,
            RunId = entity.RunId,
            Plan = plan,
            CreatedTime = entity.CreatedTime
        };
    }
}
