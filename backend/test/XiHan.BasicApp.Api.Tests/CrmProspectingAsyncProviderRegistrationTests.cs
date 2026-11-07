using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Registration contract tests for async adapters that participate in durable extraction.</summary>
public sealed class CrmProspectingAsyncProviderRegistrationTests
{
    [Fact]
    public void Registry_ShouldAcceptOnlyProviderDocumentedIdempotentSubmission()
    {
        var provider = new StubAsyncProvider(supportsIdempotency: true);
        var registry = CrmProspectingProviderRegistry.Create(
            Array.Empty<ICrmProspectingProvider>(), new[] { provider });

        Assert.Same(provider, registry.Asynchronous["stub-async"]);
    }

    [Fact]
    public void Registry_ShouldRejectAsyncProviderWithoutIdempotentSubmission()
    {
        var provider = new StubAsyncProvider(supportsIdempotency: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CrmProspectingProviderRegistry.Create(
                Array.Empty<ICrmProspectingProvider>(), new[] { provider }));

        Assert.Contains("idempotent submission", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_ShouldRejectProviderCodeCollisionAcrossCapabilities()
    {
        var sync = new StubSyncProvider();
        var async = new StubAsyncProvider(supportsIdempotency: true);

        Assert.Throws<InvalidOperationException>(() =>
            CrmProspectingProviderRegistry.Create(new[] { sync }, new[] { async }));
    }

    private sealed class StubSyncProvider : ICrmProspectingProvider
    {
        public string ProviderCode => "stub-async";

        public Task<CrmProspectingProviderResult> SearchAsync(
            CrmProspectingProviderRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CrmProspectingProviderResult(
                Array.Empty<CrmProspectingRawRecord>(), 0, Array.Empty<string>()) { IsComplete = true });
    }

    private sealed class StubAsyncProvider(bool supportsIdempotency) : ICrmProspectingAsyncExtractionProvider
    {
        public string ProviderCode => "stub-async";
        public bool SupportsIdempotentSubmission => supportsIdempotency;

        public Task<CrmProspectingExtractionSubmission> SubmitExtractionAsync(
            CrmProspectingProviderRequest request, Guid submissionIdempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CrmProspectingExtractionPollResult> PollExtractionAsync(
            CrmProspectingExtractionReference extraction, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CrmProspectingExtractionPage> FetchExtractionPageAsync(
            CrmProspectingExtractionReference extraction, string? cursor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CancelExtractionAsync(
            CrmProspectingExtractionReference extraction, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
