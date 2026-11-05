using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Structural and resource-bound validation for untrusted async extraction pages.
/// This does not declare a page persisted or complete; the repository/worker must commit records first.
/// </summary>
public static class CrmProspectingExtractionPageValidator
{
    public const int MaxRecordsPerPage = 100;
    public const int MaxCursorLength = 2048;
    public const int MaxRecordFieldLength = 5000;
    public const int MaxSerializedPageBytes = 2_000_000;

    public static bool TryValidate(
        CrmProspectingExtractionPage? page,
        out string failureCode)
    {
        failureCode = string.Empty;
        if (page is null || page.Records is null || page.Warnings is null)
        {
            failureCode = "malformed_page";
            return false;
        }

        if (page.FailureCount < 0 || page.Records.Count > MaxRecordsPerPage)
        {
            failureCode = "page_limit_exceeded";
            return false;
        }

        if (page.NextCursor is { Length: > MaxCursorLength })
        {
            failureCode = "cursor_limit_exceeded";
            return false;
        }

        if (page.IsLastPage && !page.IsComplete)
        {
            failureCode = "inconsistent_completion_marker";
            return false;
        }

        if (!page.IsLastPage && page.IsComplete)
        {
            failureCode = "inconsistent_completion_marker";
            return false;
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in page.Records)
        {
            if (record is null ||
                string.IsNullOrWhiteSpace(record.ExternalId) ||
                record.ExternalId.Length > 200 ||
                string.IsNullOrWhiteSpace(record.Source) ||
                record.Source.Length > 200 ||
                (record.Title?.Length ?? 0) > 300 ||
                (record.CompanyName?.Length ?? 0) > 200 ||
                (record.ContactName?.Length ?? 0) > 200 ||
                (record.BusinessEmail?.Length ?? 0) > 254 ||
                (record.Website?.Length ?? 0) > 500 ||
                (record.Summary?.Length ?? 0) > MaxRecordFieldLength)
            {
                failureCode = "invalid_record";
                return false;
            }

            if (!identities.Add(record.ExternalId))
            {
                failureCode = "duplicate_record_identity";
                return false;
            }

            if (record.SourceUrl is { Length: > 2000 })
            {
                failureCode = "source_url_limit_exceeded";
                return false;
            }

            if (record.SourceUrl is { Length: > 0 } sourceUrl &&
                (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                 !string.IsNullOrEmpty(uri.UserInfo)))
            {
                failureCode = "invalid_source_url";
                return false;
            }

            if (record.RawPayloadHash is { Length: > 0 } hash &&
                (hash.Length != 64 || !hash.All(Uri.IsHexDigit)))
            {
                failureCode = "invalid_payload_hash";
                return false;
            }
        }

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(page).Length;
            if (bytes > MaxSerializedPageBytes)
            {
                failureCode = "serialized_page_limit_exceeded";
                return false;
            }
        }
        catch (JsonException)
        {
            failureCode = "malformed_page";
            return false;
        }

        return true;
    }

    public static bool CanAdvanceCursor(
        CrmProspectingExtractionPage page,
        string? currentCursor)
    {
        if (!TryValidate(page, out _)) return false;
        if (page.IsLastPage)
            return page.IsComplete && page.FailureCount == 0;
        return !string.IsNullOrWhiteSpace(page.NextCursor)
            && !string.Equals(currentCursor, page.NextCursor, StringComparison.Ordinal);
    }
}
