using System.Text;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmProspectingProviderRequestRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmProspectingProviderRequest>(clientResolver), ICrmProspectingProviderRequestRepository
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.Ordinal)
    {
        "Succeeded", "Failed", "RateLimited", "Cancelled"
    };

    public async Task<SysCrmProspectingProviderRequest> CreateStartedAsync(
        long runId, string providerCode, string requestKey, DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        if (providerCode.Length > 80) throw new ArgumentOutOfRangeException(nameof(providerCode));
        if (requestKey.Length != 64 || !requestKey.All(Uri.IsHexDigit))
            throw new ArgumentException("Request key must be a 64-character SHA-256 hex digest.", nameof(requestKey));
        cancellationToken.ThrowIfCancellationRequested();

        // The unique database index is the final concurrency arbiter. If two workers race,
        // only one insert succeeds; the loser must retry before issuing the provider request.
        var previous = await CreateQueryable()
            .Where(row => row.RunId == runId && row.ProviderCode == providerCode)
            .OrderByDescending(row => row.AttemptNo)
            .Take(1)
            .ToListAsync(cancellationToken);
        var attemptNo = checked((previous.FirstOrDefault()?.AttemptNo ?? 0) + 1);

        var entity = new SysCrmProspectingProviderRequest
        {
            RunId = runId,
            ProviderCode = providerCode,
            AttemptNo = attemptNo,
            RequestKey = requestKey.ToUpperInvariant(),
            State = "Started",
            StartedAt = startedAt,
            ResultCount = 0,
            CacheHit = false
        };
        await AddAsync(entity, cancellationToken);
        return entity;
    }

    public async Task<bool> TryFinalizeAsync(
        long requestId, string terminalState, DateTimeOffset completedAt, long durationMs,
        int resultCount = 0, int? httpStatus = null, string? failureCode = null,
        string? failureSummary = null, DateTimeOffset? retryAfterAt = null,
        string? responseHash = null, bool cacheHit = false,
        CancellationToken cancellationToken = default)
    {
        if (requestId <= 0) throw new ArgumentOutOfRangeException(nameof(requestId));
        if (!TerminalStates.Contains(terminalState))
            throw new ArgumentException("A provider request can only transition to a terminal state.", nameof(terminalState));
        if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        if (resultCount < 0) throw new ArgumentOutOfRangeException(nameof(resultCount));
        if (httpStatus is < 100 or > 599) throw new ArgumentOutOfRangeException(nameof(httpStatus));
        if (responseHash is not null && (responseHash.Length != 64 || !responseHash.All(Uri.IsHexDigit)))
            throw new ArgumentException("Response hash must be a 64-character SHA-256 hex digest.", nameof(responseHash));
        cancellationToken.ThrowIfCancellationRequested();

        return await UpdateAsync(
            _ => new SysCrmProspectingProviderRequest
            {
                State = terminalState,
                CompletedAt = completedAt,
                DurationMs = durationMs,
                ResultCount = resultCount,
                HttpStatus = httpStatus,
                FailureCode = Bound(failureCode, 80),
                FailureSummary = SanitizeSummary(failureSummary),
                RetryAfterAt = retryAfterAt,
                ResponseHash = responseHash?.ToUpperInvariant(),
                CacheHit = cacheHit
            },
            row => row.BasicId == requestId && row.State == "Started",
            cancellationToken);
    }

    public async Task<SysCrmProspectingProviderRequest?> GetAttemptAsync(
        long runId, string providerCode, int attemptNo,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0) throw new ArgumentOutOfRangeException(nameof(runId));
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        if (attemptNo <= 0) throw new ArgumentOutOfRangeException(nameof(attemptNo));
        cancellationToken.ThrowIfCancellationRequested();

        var rows = await CreateQueryable()
            .Where(row => row.RunId == runId && row.ProviderCode == providerCode && row.AttemptNo == attemptNo)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count > 1)
            throw new InvalidOperationException("The tenant contains duplicate provider request attempt identities.");
        return rows.FirstOrDefault();
    }

    private static string? Bound(string? value, int maxLength)
    {
        var sanitized = SanitizeSummary(value);
        return sanitized is { Length: > 0 } ? sanitized[..Math.Min(sanitized.Length, maxLength)] : null;
    }

    private static string? SanitizeSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var builder = new StringBuilder(Math.Min(value.Length, 1000));
        foreach (var character in value)
        {
            if (!char.IsControl(character) || character is '\r' or '\n' or '\t')
                builder.Append(char.IsControl(character) ? ' ' : character);
            if (builder.Length >= 1000) break;
        }

        // Never retain URL query/fragment values in operational diagnostics.
        var sanitized = builder.ToString();
        return System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)(https?://[^\s?#]+)[^\s]*",
            "$1");
    }
}
