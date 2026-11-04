using System.Net;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed class CrmProspectingProviderException : HttpRequestException
{
    public CrmProspectingProviderException(string providerCode, HttpStatusCode statusCode,
        DateTimeOffset? retryAfterAt = null, string? safeMessage = null)
        : base(safeMessage ?? $"{providerCode} returned HTTP {(int)statusCode}.", null, statusCode)
    {
        ProviderCode = string.IsNullOrWhiteSpace(providerCode)
            ? throw new ArgumentException("Provider code is required.", nameof(providerCode)) : providerCode;
        ProviderStatusCode = (int)statusCode;
        RetryAfterAt = retryAfterAt;
        IsRateLimited = statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden;
    }

    public string ProviderCode { get; }
    public int ProviderStatusCode { get; }
    public DateTimeOffset? RetryAfterAt { get; }
    public bool IsRateLimited { get; }

    public static CrmProspectingProviderException FromResponse(string providerCode,
        HttpResponseMessage response, TimeSpan? fallbackCooldown = null, string? safeMessage = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        var status = response.StatusCode;
        DateTimeOffset? retryAt = response.Headers.RetryAfter?.Date;
        if (retryAt is null && response.Headers.RetryAfter?.Delta is { } delta)
            retryAt = DateTimeOffset.UtcNow.Add(delta);
        if (retryAt is null && fallbackCooldown is { } cooldown && cooldown > TimeSpan.Zero)
            retryAt = DateTimeOffset.UtcNow.Add(cooldown);
        if (retryAt is null && status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            retryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        return new CrmProspectingProviderException(providerCode, status, retryAt, safeMessage);
    }
}
