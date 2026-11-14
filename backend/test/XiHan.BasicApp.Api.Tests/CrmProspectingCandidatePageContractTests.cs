// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Source-level contract guards for the candidate review-queue paging slice: the paginated
/// QueryService, the retired fixed Take(500) RunCandidates shape, the export provider chain
/// (provider → registration → page button → permission) and the frontend typed client.
/// These intentionally read files instead of reflecting assemblies so the guard also covers
/// the frontend facade and the AI tool call site.
/// </summary>
public sealed class CrmProspectingCandidatePageContractTests
{
    [Fact]
    public void CandidatePageQueryService_ShouldPageUnderRecordAuthorizationWithStableOrdering()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmProspectingCandidateQueryService.cs");

        Assert.Contains("[DynamicApi(", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.Contains("[HttpPost]", service, StringComparison.Ordinal);
        Assert.Contains("GetPageAsync", service, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId", service, StringComparison.Ordinal);

        // The status filter must be composed only when actually present: the old repository
        // predicate `!status.HasValue ||` does not translate under SqlSugar/PostgreSQL (42601).
        Assert.Contains("if (input.Status.HasValue)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("!status.HasValue ||", service, StringComparison.Ordinal);
        Assert.DoesNotContain("!input.Status.HasValue ||", service, StringComparison.Ordinal);

        // Stable two-level ordering keeps consecutive pages a partition of the set.
        Assert.Contains("AddSort((SysCrmProspectingCandidate candidate) => candidate.CreatedTime, SortDirection.Descending, 0)", service, StringComparison.Ordinal);
        Assert.Contains("AddSort((SysCrmProspectingCandidate candidate) => candidate.BasicId, SortDirection.Descending, 1)", service, StringComparison.Ordinal);
        Assert.Contains("new PageResultDtoBase<CrmProspectingCandidateDto>(items, page.Page)", service, StringComparison.Ordinal);

        // No fixed row cap and no leftover Take(500) projection anywhere in the new path.
        Assert.DoesNotContain("Take(500)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLatestByRunAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidatePageContract_ShouldDeclarePaginatedShapeAndRetireRunCandidates()
    {
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Contracts", "ICrmProspectingCandidateQueryService.cs");
        Assert.Contains("GetPageAsync", contract, StringComparison.Ordinal);
        Assert.Contains("PageResultDtoBase<CrmProspectingCandidateDto>", contract, StringComparison.Ordinal);

        var dtos = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmProspectingDtos.cs");
        Assert.Contains("CrmProspectingCandidatePageQueryDto : BasicAppPRDto", dtos, StringComparison.Ordinal);
        Assert.Contains("public long RunId { get; set; }", dtos, StringComparison.Ordinal);
        Assert.Contains("public int? Status { get; set; }", dtos, StringComparison.Ordinal);

        // The retired fixed-500 route must be gone from contract, app service and repository.
        var appContract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Contracts", "ICrmProspectingAppService.cs");
        Assert.DoesNotContain("GetRunCandidatesAsync", appContract, StringComparison.Ordinal);
        var appService = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "AppServices", "CrmProspectingAppService.cs");
        Assert.DoesNotContain("GetRunCandidatesAsync", appService, StringComparison.Ordinal);

        var repositoryContract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Repositories", "ICrmProspectingCandidateRepository.cs");
        Assert.DoesNotContain("GetLatestByRunAsync", repositoryContract, StringComparison.Ordinal);
        var repository = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Repositories", "CrmProspectingCandidateRepository.cs");
        Assert.DoesNotContain("GetLatestByRunAsync", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void CandidateExportProvider_ShouldBindExecutePermissionAndRegisterWithPageButton()
    {
        var provider = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Exporting", "CrmProspectingCandidateExportProvider.cs");
        Assert.Contains("BusinessType => \"crm.prospecting.candidate\"", provider, StringComparison.Ordinal);
        Assert.Contains("RequiredPermission => CrmPermissionCodes.Execute", provider, StringComparison.Ordinal);
        Assert.Contains("QueryServiceExportProviderBase<CrmProspectingCandidatePageQueryDto, CrmProspectingCandidateDto>", provider, StringComparison.Ordinal);
        // Export rows go through the shared query service, so tenant scope, record authorization
        // and filters stay identical to the review queue it exports.
        Assert.Contains("_queryService.GetPageAsync(query, cancellationToken)", provider, StringComparison.Ordinal);

        var registration = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "CrmExportProviderRegistrationExtensions.cs");
        Assert.Contains("AddScoped<IExportProvider, CrmProspectingCandidateExportProvider>()", registration, StringComparison.Ordinal);

        var pages = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Pages", "PageRegistry.cs");
        Assert.Contains(
            "new(\"crm_prospecting.export\", \"导出\", \"crm_prospecting\", CrmPermissionCodes.Execute, 3)",
            pages,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendClient_ShouldUseTheCandidatePageDynamicApiRoute()
    {
        var client = Read("frontend", "src", "api", "modules", "crm", "prospecting.ts");
        Assert.Contains("createDynamicApiClient('CrmProspectingCandidateQuery')", client, StringComparison.Ordinal);
        Assert.Contains("post<PageResult<CrmProspectingCandidateDto>, CrmProspectingCandidatePageQueryDto>('Page', input)", client, StringComparison.Ordinal);
        Assert.DoesNotContain("'RunCandidates'", client, StringComparison.Ordinal);
    }

    [Fact]
    public void AiChatTool_ShouldPageCandidatesThroughTheSharedQueryService()
    {
        var factory = Read("backend", "src", "modules", "XiHan.BasicApp.AI", "Application", "Tools", "CrmChatToolFactory.cs");
        Assert.Contains("ICrmProspectingCandidateQueryService", factory, StringComparison.Ordinal);
        Assert.Contains("GetPageAsync(", factory, StringComparison.Ordinal);
        Assert.Contains("new PageRequestMetadata(1, CandidatePageSize)", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRunCandidatesAsync", factory, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
