namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Immutable input snapshot passed to a prospecting search provider.</summary>
public sealed record CrmProspectingProviderRequest(
    long RunId,
    string SearchObjective,
    string ProductScope,
    string TargetMarkets,
    string IdealCustomerProfile,
    string ProviderOptionsJson,
    IReadOnlyList<string> ExcludedDomains)
{
    /// <summary>
    /// Optional incremental lower bound supplied from a previous complete scan, with an overlap.
    /// It is deliberately excluded from the canonical search-scope fingerprint.
    /// </summary>
    public DateTimeOffset? CoverageSinceUtc { get; init; }
}

/// <summary>One raw result returned by a provider; it is not yet a CRM lead.</summary>
public sealed record CrmProspectingRawRecord(
    string ExternalId,
    string Source,
    string Title,
    string? CompanyName,
    string? Website,
    string? ContactName,
    string? BusinessEmail,
    string? Summary)
{
    /// <summary>Canonical source record URL, not a search-result redirect.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Provider-reported publication time, normalized to UTC by the adapter.</summary>
    public DateTimeOffset? PublishedAtUtc { get; init; }

    /// <summary>UTC time at which the provider response was retrieved.</summary>
    public DateTimeOffset? RetrievedAtUtc { get; init; }

    /// <summary>Provider payload SHA-256; do not store raw provider payloads by default.</summary>
    public string? RawPayloadHash { get; init; }

    /// <summary>Evidence classification such as direct, registry, or provider_asserted.</summary>
    public string? EvidenceType { get; init; }
}

/// <summary>Provider execution result before normalization, deduplication and human review.</summary>
public sealed record CrmProspectingProviderResult(
    IReadOnlyList<CrmProspectingRawRecord> Records,
    int FailureCount,
    IReadOnlyList<string> Warnings)
{
    /// <summary>False when the adapter stopped before exhausting the provider's available result pages.</summary>
    public bool IsComplete { get; init; } = false;
}

/// <summary>
/// Search-provider boundary. Implementations must honor cancellation, treat provider output as untrusted
/// data, and must not create or update CRM leads directly.
/// </summary>
public interface ICrmProspectingProvider
{
    string ProviderCode { get; }

    Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default);
}


/// <summary>
/// Optional capability for providers whose search results are produced by a durable remote extraction job.
/// This is deliberately separate from <see cref="ICrmProspectingProvider"/> so synchronous adapters remain
/// source- and binary-compatible and cannot accidentally be treated as asynchronous providers.
/// </summary>
public interface ICrmProspectingAsyncExtractionProvider
{
    string ProviderCode { get; }

    /// <summary>Submit one bounded extraction request and return its durable provider-side job identity.</summary>
    Task<CrmProspectingExtractionSubmission> SubmitExtractionAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Read the remote job state. Unknown provider states must map to NeedsReview, never Succeeded.</summary>
    Task<CrmProspectingExtractionPollResult> PollExtractionAsync(
        CrmProspectingExtractionReference extraction,
        CancellationToken cancellationToken = default);

    /// <summary>Fetch one bounded page after the extraction has succeeded.</summary>
    Task<CrmProspectingExtractionPage> FetchExtractionPageAsync(
        CrmProspectingExtractionReference extraction,
        string? cursor,
        CancellationToken cancellationToken = default);

    /// <summary>Request remote cancellation when supported; implementations must be idempotent.</summary>
    Task CancelExtractionAsync(
        CrmProspectingExtractionReference extraction,
        CancellationToken cancellationToken = default);
}

/// <summary>Opaque provider-side extraction identity. Values are untrusted and must be length-limited before persistence.</summary>
public sealed record CrmProspectingExtractionReference(
    string ProviderCode,
    string ProviderExtractionId);

/// <summary>Remote job accepted by the provider; acceptance is not evidence that results are ready.</summary>
public sealed record CrmProspectingExtractionSubmission(
    CrmProspectingExtractionReference Extraction,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    TimeSpan? PollAfter,
    string? SafeMetadataJson);

/// <summary>Normalized remote extraction status and bounded retry guidance.</summary>
public sealed record CrmProspectingExtractionPollResult(
    CrmProspectingExtractionStatus Status,
    DateTimeOffset? RetryAtUtc,
    string? FailureCode,
    string? SafeFailureSummary);

/// <summary>One bounded result page; cursor advancement is permitted only after page persistence succeeds.</summary>
public sealed record CrmProspectingExtractionPage(
    IReadOnlyList<CrmProspectingRawRecord> Records,
    string? NextCursor,
    bool IsLastPage,
    bool IsComplete,
    int FailureCount,
    IReadOnlyList<string> Warnings);

/// <summary>Provider-independent extraction lifecycle states.</summary>
public enum CrmProspectingExtractionStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Expired = 4,
    Cancelled = 5,
    NeedsReview = 6
}
