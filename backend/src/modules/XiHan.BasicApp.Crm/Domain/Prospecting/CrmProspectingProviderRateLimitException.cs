namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>Structured provider throttling signal used for durable request-ledger classification.</summary>
public sealed class CrmProspectingProviderRateLimitException : Exception
{
    public int HttpStatus { get; }
    public DateTimeOffset? RetryAfterAt { get; }

    public CrmProspectingProviderRateLimitException(
        string message, int httpStatus, DateTimeOffset? retryAfterAt)
        : base(message)
    {
        if (httpStatus is not (403 or 429 or 503))
            throw new ArgumentOutOfRangeException(nameof(httpStatus), "Only documented throttling statuses are accepted.");
        HttpStatus = httpStatus;
        RetryAfterAt = retryAfterAt;
    }
}
