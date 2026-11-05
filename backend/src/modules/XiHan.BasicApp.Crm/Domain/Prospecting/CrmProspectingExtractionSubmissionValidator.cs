using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Validates untrusted asynchronous submission acknowledgements before they are persisted.
/// Provider identifiers are opaque values, never URLs or routing instructions.
/// </summary>
public static class CrmProspectingExtractionSubmissionValidator
{
    public const int MaxProviderExtractionIdLength = 512;
    public const int MaxSafeMetadataJsonLength = 4096;
    public const int MaxPollAfterSeconds = 86400;

    public static bool TryValidate(
        CrmProspectingExtractionSubmission? submission,
        string expectedProviderCode,
        out string failureCode)
    {
        failureCode = string.Empty;
        if (submission is null || submission.Extraction is null ||
            string.IsNullOrWhiteSpace(expectedProviderCode))
        {
            failureCode = "malformed_submission";
            return false;
        }

        var expectedCode = expectedProviderCode.Trim();
        var actualCode = submission.Extraction.ProviderCode;
        if (string.IsNullOrWhiteSpace(actualCode) ||
            actualCode.Length > 80 ||
            !string.Equals(actualCode.Trim(), expectedCode, StringComparison.OrdinalIgnoreCase))
        {
            failureCode = "provider_identity_mismatch";
            return false;
        }

        var extractionId = submission.Extraction.ProviderExtractionId;
        if (string.IsNullOrWhiteSpace(extractionId) ||
            extractionId.Length > MaxProviderExtractionIdLength ||
            extractionId.Any(char.IsControl))
        {
            failureCode = "invalid_provider_extraction_id";
            return false;
        }

        if (submission.ExpiresAtUtc is not null &&
            submission.ExpiresAtUtc.Value <= submission.SubmittedAtUtc)
        {
            failureCode = "invalid_provider_expiry";
            return false;
        }

        if (submission.PollAfter is { } pollAfter &&
            (pollAfter < TimeSpan.Zero || pollAfter.TotalSeconds > MaxPollAfterSeconds))
        {
            failureCode = "invalid_poll_after";
            return false;
        }

        if (submission.SafeMetadataJson is { } metadata)
        {
            if (metadata.Length > MaxSafeMetadataJsonLength)
            {
                failureCode = "safe_metadata_limit_exceeded";
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(metadata);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    failureCode = "invalid_safe_metadata";
                    return false;
                }
            }
            catch (JsonException)
            {
                failureCode = "invalid_safe_metadata";
                return false;
            }
        }

        return true;
    }
}
