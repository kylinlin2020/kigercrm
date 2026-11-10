using System.Text.Json;
using Moq;
using XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Validates provider plan input before an immutable strategy version can be allocated.</summary>
public sealed class CrmProspectingProviderPlanValidationTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("[\"\"]")]
    [InlineData("[\"   \"]")]
    [InlineData("[\"fake\",\"FAKE\"]")]
    [InlineData("[\"fake\",7]")]
    [InlineData("[null]")]
    public async Task StrategyCreation_ShouldRejectEmptyBlankDuplicateOrNonStringProviderEntries(string providerPlanJson)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateStrategyVersionAsync(CreateStrategy(providerPlanJson)));
    }

    [Fact]
    public async Task StrategyCreation_ShouldRejectProviderCodesLongerThanTheWorkerContract()
    {
        var providerCode = new string('p', 81);
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.CreateStrategyVersionAsync(CreateStrategy(JsonSerializer.Serialize(new[] { providerCode }))));
    }

    [Fact]
    public async Task StrategyCreation_ShouldRejectProviderPlansLargerThanTheBoundedExecutionPlan()
    {
        var providerPlan = JsonSerializer.Serialize(Enumerable.Range(0, 33).Select(index => $"provider-{index}"));
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.CreateStrategyVersionAsync(CreateStrategy(providerPlan)));
    }

    [Fact]
    public async Task StrategyCreation_ShouldRejectControlCharactersInProviderCodes()
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.CreateStrategyVersionAsync(CreateStrategy(JsonSerializer.Serialize(new[] { "fake\nprovider" }))));
    }

    [Fact]
    public async Task InvalidProviderPlan_ShouldNotReadCampaignOrAdvanceStrategyVersion()
    {
        var campaigns = new Mock<ICrmProspectingCampaignRepository>(MockBehavior.Strict);
        var strategies = new Mock<ICrmProspectingStrategyRepository>(MockBehavior.Strict);
        var runs = new Mock<ICrmProspectingRunRepository>(MockBehavior.Strict);
        var service = new CrmProspectingDomainService(campaigns.Object, strategies.Object, runs.Object);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.CreateStrategyVersionAsync(CreateStrategy("[\"provider-a\",\"PROVIDER-A\"]")));

        campaigns.VerifyNoOtherCalls();
        strategies.VerifyNoOtherCalls();
        runs.VerifyNoOtherCalls();
    }

    private static CrmProspectingDomainService CreateService()
        => new(
            new Mock<ICrmProspectingCampaignRepository>().Object,
            new Mock<ICrmProspectingStrategyRepository>().Object,
            new Mock<ICrmProspectingRunRepository>().Object);

    private static SysCrmProspectingStrategy CreateStrategy(string providerPlanJson)
        => new()
        {
            CampaignId = 1,
            Name = "Provider validation",
            SearchObjective = "Validate configured provider adapters",
            ProviderPlanJson = providerPlanJson,
            ExcludedDomainsJson = "[]"
        };
}
