// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.QueryServices;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// M1.1: the pre-existing deterministic Super Search planner is exposed as a read-only Dynamic API
/// preview. These tests pin validation, exclusion propagation, determinism, explicit DTO mapping,
/// pipeline authorization, the no-side-effect boundary and the mirrored frontend contract.
/// </summary>
public sealed class CrmProspectingSuperSearchPreviewTests
{
    private static CrmProspectingSuperSearchPreviewInputDto Input(int depth = 0, int? rounds = null) => new()
    {
        Products = ["  pump  ", "PUMP", "valve"],
        Markets = ["DE", "BR"],
        CustomerTypes = ["distributor", "importer"],
        Industries = [" industrial ", "oil & gas"],
        ProviderCodes = ["ProviderA", "providerb"],
        ExcludedTerms = ["  competitor ", "spam"],
        ExcludedDomains = [".example.com", "test.org."],
        Depth = depth,
        RequestedRounds = rounds
    };

    [Fact]
    public async Task Preview_ShouldValidateDepthAndRequestedRoundLimits()
    {
        var service = new CrmProspectingSuperSearchQueryService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.PreviewSuperSearchAsync(Input(depth: 99)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.PreviewSuperSearchAsync(Input(depth: 0, rounds: 5)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.PreviewSuperSearchAsync(Input(depth: 2, rounds: 17)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewSuperSearchAsync(
            new CrmProspectingSuperSearchPreviewInputDto { Depth = 0 }));

        var extreme = await service.PreviewSuperSearchAsync(Input(depth: 2, rounds: 16));
        Assert.Equal(16, extreme.MaximumRounds);
        Assert.Equal(16, extreme.PlannedRounds);
        Assert.Equal(16, extreme.Rounds.Count);
    }

    [Fact]
    public async Task Preview_ShouldBeDeterministicAndCarryExclusionsIntoEveryQueryCell()
    {
        var service = new CrmProspectingSuperSearchQueryService();

        var first = await service.PreviewSuperSearchAsync(Input(rounds: 3));
        var second = await service.PreviewSuperSearchAsync(Input(rounds: 3));

        Assert.Equal(first.ScopeFingerprint, second.ScopeFingerprint);
        Assert.Equal(64, first.ScopeFingerprint.Length);
        Assert.Equal(4, first.MaximumRounds);
        Assert.Equal(3, first.PlannedRounds);
        Assert.Equal(2, first.ProviderCount);
        Assert.Equal(6, first.TotalQueryCells);
        Assert.Equal(new[] { 1, 2, 3 }, first.Rounds.Select(round => round.RoundNumber));
        Assert.Equal(first.Rounds[0].Fingerprint, second.Rounds[0].Fingerprint);

        // Themes rotate across rounds; exclusions and the normalized scope survive into every cell.
        Assert.Equal(3, first.Rounds.Select(round => round.ThemeCode).Distinct().Count());
        Assert.All(first.Rounds, round =>
        {
            Assert.Equal(new[] { "competitor", "spam" }, round.ExcludedTerms);
            Assert.Equal(new[] { "example.com", "test.org" }, round.ExcludedDomains);
            Assert.Equal(64, round.Fingerprint.Length);
            Assert.Equal(2, round.QueryCells.Count);
            Assert.All(round.QueryCells, cell =>
            {
                Assert.Equal(round.Market, cell.Market);
                Assert.Equal(round.CustomerType, cell.CustomerType);
                Assert.Equal(round.ThemeCode, cell.ThemeCode);
                Assert.Contains("pump", cell.QueryText, StringComparison.Ordinal);
                Assert.Contains("valve", cell.QueryText, StringComparison.Ordinal);
                Assert.DoesNotContain("  ", cell.QueryText, StringComparison.Ordinal);
                Assert.Equal(64, cell.Fingerprint.Length);
            });
        });
        Assert.Equal(
            new[] { "providera", "providerb" },
            first.Rounds[0].QueryCells.Select(cell => cell.ProviderCode).OrderBy(code => code).ToArray());
    }

    [Fact]
    public async Task Preview_ShouldPropagateCancellationInsteadOfReturningAPlan()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CrmProspectingSuperSearchQueryService().PreviewSuperSearchAsync(Input(), cancellation.Token));
    }

    [Fact]
    public void PreviewService_ShouldBeAuthorizedReadOnlyAndMirroredInTheFrontend()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "QueryServices", "CrmProspectingSuperSearchQueryService.cs");
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Contracts", "ICrmProspectingSuperSearchQueryService.cs");
        var dtos = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Dtos", "CrmProspectingDtos.cs");
        var mapper = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Mappers", "CrmProspectingMapper.cs");
        var frontendTypes = Read("frontend", "src", "api", "modules", "crm", "prospecting.types.ts");
        var frontendClient = Read("frontend", "src", "api", "modules", "crm", "prospecting.ts");

        // Pipeline authorization and POST semantics on the dynamic API entry point.
        Assert.Contains("[DynamicApi(Group = \"BasicApp.Crm\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.Contains("[HttpPost]", service, StringComparison.Ordinal);
        Assert.Contains("ICrmProspectingSuperSearchQueryService", contract, StringComparison.Ordinal);

        // No repositories, no writes, no Run creation: the existing pipeline stays the only executor.
        Assert.DoesNotContain("Repository", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("StartRunAsync", service, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingSuperSearchPlanner.Create", service, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", service, StringComparison.Ordinal);

        // Explicit field-by-field mapping instead of reflection or dual DTO fields.
        Assert.Contains("ToSuperSearchPreviewDto", mapper, StringComparison.Ordinal);
        Assert.Contains("ScopeFingerprint = plan.ScopeFingerprint", mapper, StringComparison.Ordinal);
        Assert.Contains("class CrmProspectingSuperSearchPreviewInputDto", dtos, StringComparison.Ordinal);
        Assert.Contains("class CrmProspectingSuperSearchPreviewDto", dtos, StringComparison.Ordinal);

        // Typed frontend mirror of the wire contract and its Dynamic API route.
        Assert.Contains("export enum CrmProspectingSuperSearchDepth", frontendTypes, StringComparison.Ordinal);
        Assert.Contains("export interface CrmProspectingSuperSearchPreviewDto", frontendTypes, StringComparison.Ordinal);
        Assert.Contains("'PreviewSuperSearch'", frontendClient, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingSuperSearchQuery", frontendClient, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseObjective_ShouldMapBilingualMarkersIntoBoundedPlanInput()
    {
        var service = new CrmProspectingSuperSearchQueryService();
        var result = await service.ParseSuperSearchObjectiveAsync(new CrmProspectingSuperSearchObjectiveParseDto
        {
            Objective = """
                产品：工业泵, 阀门
                市场：德国, BR
                客户类型：经销商, importer
                行业：制造业
                数据源：provider-a, Provider-B
                排除：spam, competitor
                排除域名：.example.com
                深度：深入
                轮次：3
                """
        });

        Assert.Equal(new[] { "工业泵", "阀门" }, result.Input.Products);
        Assert.Equal(new[] { "DE", "BR" }, result.Input.Markets);
        Assert.Equal(new[] { "经销商", "importer" }, result.Input.CustomerTypes);
        Assert.Equal(new[] { "制造业" }, result.Input.Industries);
        Assert.Equal(new[] { "provider-a", "provider-b" }, result.Input.ProviderCodes);
        Assert.Equal(new[] { "spam", "competitor" }, result.Input.ExcludedTerms);
        Assert.Equal(new[] { "example.com" }, result.Input.ExcludedDomains);
        Assert.Equal((int)CrmProspectingSuperSearchDepth.Deep, result.Input.Depth);
        Assert.Equal(3, result.Input.RequestedRounds);
        Assert.Equal(9, result.Matches.Count);
        Assert.Equal("市场", result.Matches.Single(match => match.Field == "markets").Marker);
        Assert.Equal(new[] { "DE", "BR" }, result.Matches.Single(match => match.Field == "markets").Values);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ParseObjective_ShouldRejectAmbiguityAndInjectionLikeInput()
    {
        var service = new CrmProspectingSuperSearchQueryService();
        Task Parse(string objective) => service.ParseSuperSearchObjectiveAsync(
            new CrmProspectingSuperSearchObjectiveParseDto { Objective = objective });

        await Assert.ThrowsAsync<ArgumentException>(() => Parse("随便：x"));
        await Assert.ThrowsAsync<ArgumentException>(() => Parse("市场：DE\n市场：BR"));
        await Assert.ThrowsAsync<ArgumentException>(() => Parse("找德国的工业泵"));
        await Assert.ThrowsAsync<ArgumentException>(() => Parse("市场：DE 排除：spam"));
        await Assert.ThrowsAsync<ArgumentException>(() => Parse("产品：泵\n深度：超级"));
        await Assert.ThrowsAsync<ArgumentException>(() => Parse("产品：泵\n轮次：99"));
        await Assert.ThrowsAsync<ArgumentException>(() => Parse("产品：泵\u0001"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Parse(new string('x', 2001)));
    }

    [Fact]
    public async Task ParseObjective_ShouldBeDeterministicAndMirrorTheFrontendContract()
    {
        var service = new CrmProspectingSuperSearchQueryService();
        var input = new CrmProspectingSuperSearchObjectiveParseDto { Objective = "产品：泵\n市场：德国\n数据源：provider-a" };

        var first = await service.ParseSuperSearchObjectiveAsync(input);
        var second = await service.ParseSuperSearchObjectiveAsync(input);
        Assert.Equal(first.Input.Products, second.Input.Products);
        Assert.Equal(first.Matches.Count, second.Matches.Count);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ParseSuperSearchObjectiveAsync(input, cancellation.Token));

        var serviceSource = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "QueryServices", "CrmProspectingSuperSearchQueryService.cs");
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Contracts", "ICrmProspectingSuperSearchQueryService.cs");
        var mapper = Read("backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "Mappers", "CrmProspectingMapper.cs");
        var frontendTypes = Read("frontend", "src", "api", "modules", "crm", "prospecting.types.ts");
        var frontendClient = Read("frontend", "src", "api", "modules", "crm", "prospecting.ts");

        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]\n    [HttpPost]\n    public Task<CrmProspectingSuperSearchObjectiveParseResultDto> ParseSuperSearchObjectiveAsync", serviceSource, StringComparison.Ordinal);
        Assert.Contains("CrmProspectingObjectiveParser.Parse(input.Objective)", serviceSource, StringComparison.Ordinal);
        Assert.Contains("ParseSuperSearchObjectiveAsync", contract, StringComparison.Ordinal);
        Assert.Contains("ToSuperSearchObjectiveParseResultDto", mapper, StringComparison.Ordinal);
        Assert.Contains("export interface CrmProspectingSuperSearchObjectiveParseResultDto", frontendTypes, StringComparison.Ordinal);
        Assert.Contains("'ParseSuperSearchObjective'", frontendClient, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
