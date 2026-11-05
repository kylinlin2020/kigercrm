using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>
/// Guards the Opportunity domain invariants that tie a pipeline record to a customer
/// and an optional contact belonging to that same customer.
/// </summary>
public sealed class CrmOpportunityRelationshipWiringTests
{
    [Fact]
    public void CreateAndUpdate_ShouldValidateCustomerAndContactOwnershipBeforePersisting()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmOpportunityDomainService.cs");

        var create = source.IndexOf("public async Task<SysCrmOpportunity> CreateAsync", StringComparison.Ordinal);
        var update = source.IndexOf("public async Task<SysCrmOpportunity> UpdateAsync", StringComparison.Ordinal);
        var ensure = source.IndexOf("private async Task EnsureReferencesAsync", StringComparison.Ordinal);

        Assert.True(create >= 0 && update > create && ensure > update);
        Assert.Contains("await EnsureReferencesAsync(command.CustomerId, command.PrimaryContactId", source, StringComparison.Ordinal);
        Assert.Contains("await EnsureReferencesAsync(command.CustomerId, command.PrimaryContactId", source[update..], StringComparison.Ordinal);
        Assert.Contains("Customer does not exist in the current tenant.", source, StringComparison.Ordinal);
        Assert.Contains("Primary contact does not exist in the current tenant.", source, StringComparison.Ordinal);
        Assert.Contains("if (contact.CustomerId != customerId)", source, StringComparison.Ordinal);
        Assert.Contains("Primary contact must belong to the selected customer.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpportunityValidation_ShouldRejectInvalidCurrencyAndLostStageWithoutReason()
    {
        var source = Read(
            "backend", "src", "modules", "XiHan.BasicApp.Crm", "Domain",
            "DomainServices", "Implementations", "CrmOpportunityDomainService.cs");

        Assert.Contains("normalizedCurrency.Length != 3", source, StringComparison.Ordinal);
        Assert.Contains("normalizedCurrency.Any(c => c < 'A' || c > 'Z')", source, StringComparison.Ordinal);
        Assert.Contains("parsedStage == CrmOpportunityStage.Lost && string.IsNullOrWhiteSpace(lostReason)", source, StringComparison.Ordinal);
        Assert.Contains("Lost reason is required when an opportunity is Lost.", source, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
