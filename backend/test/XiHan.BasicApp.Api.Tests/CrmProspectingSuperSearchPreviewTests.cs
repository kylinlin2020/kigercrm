// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Application.QueryServices;

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

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
