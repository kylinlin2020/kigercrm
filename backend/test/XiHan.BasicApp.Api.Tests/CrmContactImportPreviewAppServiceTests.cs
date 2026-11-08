using System.Text;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Importing;
using XiHan.BasicApp.Crm.Domain.Repositories;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmContactImportPreviewAppServiceTests
{
    [Fact]
    public async Task PreviewAsync_ShouldFlagCustomerIdsMissingFromCurrentTenantLookup()
    {
        var lookup = new FakeCustomerLookupRepository(new HashSet<long> { 101 });
        var service = new CrmContactImportPreviewAppService(lookup);
        const string csv = "CustomerId,FirstName,LastName\n101,Ada,Lovelace\n202,Grace,Hopper\n";

        var result = await service.PreviewAsync(new CrmCsvImportPreviewRequestDto
        {
            FileBytes = Encoding.UTF8.GetBytes(csv)
        });

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Contains(result.PreviewRows[1].Errors, error => error.Contains("202", StringComparison.Ordinal));
        Assert.Equal(new long[] { 101, 202 }, lookup.LastRequestedIds);
    }

    [Fact]
    public async Task PreviewAsync_ShouldValidateAllRowsButReturnOnlyFirstHundred()
    {
        var existingIds = Enumerable.Range(1, 100).Select(value => (long)value).ToHashSet();
        var lookup = new FakeCustomerLookupRepository(existingIds);
        var service = new CrmContactImportPreviewAppService(lookup);
        var rows = Enumerable.Range(1, 100)
            .Select(value => $"{value},First{value},Last{value}")
            .Append("999,Last,Row");
        var csv = "CustomerId,FirstName,LastName\n" + string.Join("\n", rows) + "\n";

        var result = await service.PreviewAsync(new CrmCsvImportPreviewRequestDto
        {
            FileBytes = Encoding.UTF8.GetBytes(csv)
        });

        Assert.Equal(101, result.TotalRows);
        Assert.Equal(100, result.ValidRows);
        Assert.Equal(1, result.InvalidRows);
        Assert.Equal(100, result.PreviewRows.Count);
        Assert.Equal(101, lookup.LastRequestedIds.Count);
    }

    private sealed class FakeCustomerLookupRepository(IReadOnlySet<long> existingIds)
        : ICrmCustomerLookupRepository
    {
        public IReadOnlyCollection<long> LastRequestedIds { get; private set; } = [];

        public Task<IReadOnlySet<long>> GetExistingIdsAsync(
            IReadOnlyCollection<long> customerIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestedIds = customerIds.ToArray();
            IReadOnlySet<long> result = customerIds.Where(existingIds.Contains).ToHashSet();
            return Task.FromResult(result);
        }
    }
}
