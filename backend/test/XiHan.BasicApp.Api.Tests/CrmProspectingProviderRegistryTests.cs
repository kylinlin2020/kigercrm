using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingProviderRegistryTests
{
    [Fact]
    public void Create_NormalizesProviderCodesAndKeepsCapabilitiesSeparate()
    {
        var sync = new SyncProvider("  PUBLIC_REGISTRY ");
        var asyncProvider = new AsyncProvider("ASYNC_EXPORT");

        var registry = CrmProspectingProviderRegistry.Create([sync], [asyncProvider]);

        Assert.Same(sync, registry.Synchronous["public_registry"]);
        Assert.Same(asyncProvider, registry.Asynchronous["async_export"]);
    }

    [Theory]
    [InlineData("duplicate", "DUPLICATE")]
    [InlineData("  same-code ", "same-code")]
    public void Create_RejectsDuplicateCodesAcrossCapabilityContracts(string syncCode, string asyncCode)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CrmProspectingProviderRegistry.Create(
                [new SyncProvider(syncCode)],
                [new AsyncProvider(asyncCode)]));

        Assert.Contains("Duplicate prospecting provider code", exception.Message);
    }

    [Fact]
    public void Create_RejectsBlankOrOversizedCodes()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CrmProspectingProviderRegistry.Create([new SyncProvider("  ")], []));
        Assert.Throws<InvalidOperationException>(() =>
            CrmProspectingProviderRegistry.Create([], [new AsyncProvider(new string('x', 81))]));
    }

    private sealed class SyncProvider(string providerCode) : ICrmProspectingProvider
    {
        public string ProviderCode { get; } = providerCode;

        public Task<CrmProspectingProviderResult> SearchAsync(
            CrmProspectingProviderRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CrmProspectingProviderResult([], 0, []));
    }

    private sealed class AsyncProvider(string providerCode) : ICrmProspectingAsyncExtractionProvider
    {
        public string ProviderCode { get; } = providerCode;

        public Task<CrmProspectingExtractionSubmission> SubmitExtractionAsync(
            CrmProspectingProviderRequest request,
            Guid submissionIdempotencyKey,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CrmProspectingExtractionPollResult> PollExtractionAsync(
            CrmProspectingExtractionReference extraction,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CrmProspectingExtractionPage> FetchExtractionPageAsync(
            CrmProspectingExtractionReference extraction,
            string? cursor,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CancelExtractionAsync(
            CrmProspectingExtractionReference extraction,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
