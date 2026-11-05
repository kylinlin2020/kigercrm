using System.Runtime.CompilerServices;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmRelatedRecordDataScopeTests
{
    [Fact]
    public void ContactCommands_ShouldRequireAccessibleCustomer()
    {
        var source = Read("CrmContactAppService.cs");
        Assert.Contains("EnsureCustomerAccessibleAsync(input.CustomerId", source, StringComparison.Ordinal);
        Assert.Contains("customers.GetByIdAsync(customerId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpportunityCommands_ShouldRequireAccessibleCustomerAndContact()
    {
        var source = Read("CrmOpportunityAppService.cs");
        Assert.Contains("EnsureRelatedRecordsAccessibleAsync(input.CustomerId, input.PrimaryContactId", source, StringComparison.Ordinal);
        Assert.Contains("customers.GetByIdAsync(customerId", source, StringComparison.Ordinal);
        Assert.Contains("contacts.GetByIdAsync(contactId.Value", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, contact.OwnerUserId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityCommands_ShouldRequireAccessibleRelatedRecordForEverySupportedType()
    {
        var source = Read("CrmActivityAppService.cs");
        Assert.Contains("EnsureRelatedRecordAccessibleAsync(input.RelatedEntityType, input.RelatedEntityId", source, StringComparison.Ordinal);
        Assert.Contains("\"customer\" =>", source, StringComparison.Ordinal);
        Assert.Contains("\"contact\" =>", source, StringComparison.Ordinal);
        Assert.Contains("\"lead\" =>", source, StringComparison.Ordinal);
        Assert.Contains("\"opportunity\" =>", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, ownerUserId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskCommands_ShouldRequireAccessibleOptionalRelatedRecord()
    {
        var source = Read("CrmTaskAppService.cs");
        Assert.Contains("EnsureRelatedRecordAccessibleAsync(input.RelatedEntityType, input.RelatedEntityId", source, StringComparison.Ordinal);
        Assert.Contains("if (relatedType is null && relatedId is null) return;", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, ownerUserId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuoteCreateAndRevision_ShouldRequireAccessibleCustomerContactAndOpportunity()
    {
        var source = Read("CrmQuoteAppService.cs");
        Assert.Contains("EnsureRelatedRecordsAccessibleAsync(input.CustomerId, input.ContactId, input.OpportunityId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRelatedRecordsAccessibleAsync(source.CustomerId, source.ContactId, source.OpportunityId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRelatedRecordsAccessibleAsync(quote.CustomerId, quote.ContactId, quote.OpportunityId", source, StringComparison.Ordinal);
        Assert.Contains("customers.GetByIdAsync(customerId", source, StringComparison.Ordinal);
        Assert.Contains("contacts.GetByIdAsync(contactId.Value", source, StringComparison.Ordinal);
        Assert.Contains("opportunities.GetByIdAsync(opportunityId.Value", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, customer.OwnerUserId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, contact.OwnerUserId", source, StringComparison.Ordinal);
        Assert.Contains("EnsureRecordAccessibleAsync(dataScope, opportunity.OwnerUserId", source, StringComparison.Ordinal);
    }

    private static string Read(string fileName) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(
            RepositoryRoot(), "backend", "src", "modules", "XiHan.BasicApp.Crm",
            "Application", "AppServices", fileName)));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
