// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Source-level contract guards for the Run monitor endpoint. These intentionally read files
/// instead of reflecting over assemblies so the guard also covers the frontend typed client.
/// </summary>
public sealed class CrmProspectingRunMonitorContractTests
{
    [Fact]
    public void RunMonitorEndpoint_ShouldProjectProgressUnderRecordAuthorization()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmProspectingRunQueryService.cs");
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Contracts", "ICrmProspectingRunQueryService.cs");
        var dtos = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "Dtos", "CrmProspectingDtos.cs");

        Assert.Contains("GetRunMonitorAsync", contract, StringComparison.Ordinal);
        Assert.Contains("GetRunMonitorAsync", service, StringComparison.Ordinal);
        Assert.Contains("[DynamicApi(", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Read)]", service, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, campaign.OwnerUserId", service, StringComparison.Ordinal);
        Assert.Contains("GetByRunAsync(runId", service, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunProgressBuilder.Build", service, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunMonitorDto", dtos, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunProgressDto", dtos, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingRunProviderExecutionDto", dtos, StringComparison.Ordinal);
        Assert.Contains("Percent", dtos, StringComparison.Ordinal);
        Assert.Contains("FailureCode", dtos, StringComparison.Ordinal);
    }

    [Fact]
    public void RunProgressBuilder_ShouldCountTerminalStepsIncludingNeedsReview()
    {
        var builder = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application", "QueryServices", "CrmProspectingRunProgressBuilder.cs");
        Assert.Contains("case NeedsReviewState:", builder, StringComparison.Ordinal);
        Assert.Contains("case SkippedState:", builder, StringComparison.Ordinal);
        Assert.Contains("progress.CompletedSteps = progress.SucceededSteps + progress.FailedSteps", builder, StringComparison.Ordinal);
        Assert.Contains("+ progress.SkippedSteps + progress.NeedsReviewSteps", builder, StringComparison.Ordinal);
        Assert.Contains("progress.TotalSteps == 0", builder, StringComparison.Ordinal);
        // Unknown states must not be silently counted as running or completed.
        Assert.DoesNotContain("default:", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendClient_ShouldUseTheRunMonitorDynamicApiRoute()
    {
        var client = Read("frontend", "src", "api", "modules", "crm", "prospecting.ts");
        Assert.Contains("createDynamicApiClient('CrmProspectingRunQuery')", client, StringComparison.Ordinal);
        Assert.Contains("'RunMonitor'", client, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendEnums_ShouldMatchBackendRunAndCandidateStatuses()
    {
        var frontendTypes = Read("frontend", "src", "api", "modules", "crm", "prospecting.types.ts");
        var runStatus = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "Enums", "CrmProspectingRunStatus.cs");
        var candidateStatus = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain", "Entities", "Enums", "CrmProspectingCandidateStatus.cs");

        Assert.Contains("WaitingForExtractions = 7", runStatus, StringComparison.Ordinal);
        Assert.Contains("WaitingForExtractions = 7", frontendTypes, StringComparison.Ordinal);
        Assert.Contains("Converting = 4", candidateStatus, StringComparison.Ordinal);
        Assert.Contains("Converted = 5", candidateStatus, StringComparison.Ordinal);
        Assert.Contains("Converting = 4", frontendTypes, StringComparison.Ordinal);
        Assert.Contains("Converted = 5", frontendTypes, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
