using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// HTTPS-only, DNS-pinned, size-bounded page fetch. The pinned handler refuses non-public DNS
/// answers and redirects to any host other than the approved one, so a redirect cannot smuggle the
/// fetch into an internal network. No content is interpreted as CRM fact; only the hash, status and
/// title are returned for operator review.
/// </summary>
public sealed class CrmProspectingPinnedPageFetcher : ICrmProspectingPageFetcher
{
    private const int MaximumBytes = 256 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<CrmProspectingPageFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return new CrmProspectingPageFetchResult(url, 0, null, string.Empty, 0, "invalid_url");
        var assessment = CrmProspectingWebsiteDiscoveryPolicy.Assess(new CrmProspectingWebsiteCandidateInput(
            uri.IdnHost,
            url,
            "website-probe",
            CrmProspectingWebsiteSourceKind.ManualReview));
        if (!assessment.AcceptedForReview)
            return new CrmProspectingPageFetchResult(url, 0, null, string.Empty, 0, assessment.ReasonCode);

        try
        {
            using var handler = CrmProspectingHttpHandlerFactory.Create(uri.IdnHost);
            using var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout };
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.UserAgent.ParseAdd("KigerCRM-Prospecting/1.0");
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            if (!string.Equals(finalUri.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase))
                return new CrmProspectingPageFetchResult(finalUri.ToString(), 0, null, string.Empty, 0, "cross_host_redirect");
            var finalAssessment = CrmProspectingWebsiteDiscoveryPolicy.Assess(new CrmProspectingWebsiteCandidateInput(
                finalUri.IdnHost,
                finalUri.ToString(),
                "website-probe",
                CrmProspectingWebsiteSourceKind.ManualReview));
            if (!finalAssessment.AcceptedForReview)
                return new CrmProspectingPageFetchResult(finalUri.ToString(), 0, null, string.Empty, 0, finalAssessment.ReasonCode);

            var (body, byteCount) = await ReadBoundedAsync(response, cancellationToken);
            var title = ReadTitle(body);
            var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
            return new CrmProspectingPageFetchResult(
                finalUri.ToString(), (int)response.StatusCode, title, hash, byteCount, "ok");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new CrmProspectingPageFetchResult(uri.ToString(), 0, null, string.Empty, 0, "fetch_failed");
        }
    }

    private static async Task<(byte[] Body, int ByteCount)> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[8192];
        using var memory = new MemoryStream();
        var total = 0;
        while (total < MaximumBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaximumBytes - total)), cancellationToken);
            if (read == 0) break;
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }
        return (memory.ToArray(), total);
    }

    private static string? ReadTitle(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        var match = Regex.Match(text, "<title[^>]*>([\\s\\S]*?)</title>", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        var title = Regex.Replace(match.Groups[1].Value, "\\s+", " ").Trim();
        return title.Length == 0 ? null : title[..Math.Min(title.Length, 200)];
    }
}
