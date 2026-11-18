namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Bounded page fetch outcome. A non-success result carries a reason instead of a fake page.</summary>
public sealed record CrmProspectingPageFetchResult(
    string FinalUrl,
    int StatusCode,
    string? Title,
    string BodyHash,
    int ByteCount,
    string ReasonCode)
{
    public bool Succeeded => StatusCode is >= 200 and < 300 && ReasonCode == "ok";
}

/// <summary>
/// Bounded, DNS-pinned page fetch used only for review-only website evidence. Implementations must
/// cap response size and time, reject cross-host redirects and revalidate the final URL.
/// </summary>
public interface ICrmProspectingPageFetcher
{
    Task<CrmProspectingPageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}
