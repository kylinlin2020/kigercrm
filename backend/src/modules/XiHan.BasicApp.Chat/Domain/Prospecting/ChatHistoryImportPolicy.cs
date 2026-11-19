using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;

namespace XiHan.BasicApp.Chat.Domain.Prospecting;

public sealed record ChatHistoryImportSourceRow(
    string ExternalId,
    string Sender,
    string Recipient,
    string Content,
    string SentAt);

public sealed record ChatHistoryImportVerdict(
    ChatHistoryImportRowStatus Status,
    string? Reason,
    DateTimeOffset SentAtUtc,
    string ContentHash);

/// <summary>
/// Deterministic history-import validation and dedupe. The verdict is computed before any write:
/// an invalid row never becomes an imported record, and a repeated external id is never silently
/// re-imported into the same batch.
/// </summary>
public static class ChatHistoryImportPolicy
{
    public const int MaximumRowsPerBatch = 500;

    public static IReadOnlyList<ChatHistoryImportVerdict> Evaluate(
        IReadOnlyList<ChatHistoryImportSourceRow> rows,
        Func<string, bool> alreadyImported)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(alreadyImported);
        if (rows.Count > MaximumRowsPerBatch)
            throw new ArgumentOutOfRangeException(nameof(rows), $"At most {MaximumRowsPerBatch} rows per batch.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var verdicts = new List<ChatHistoryImportVerdict>(rows.Count);
        foreach (var row in rows)
        {
            if (row is null)
            {
                verdicts.Add(new ChatHistoryImportVerdict(ChatHistoryImportRowStatus.Invalid,
                    "row_missing", DateTimeOffset.UnixEpoch, string.Empty));
                continue;
            }
            var reason = Validate(row);
            if (reason is not null)
            {
                verdicts.Add(new ChatHistoryImportVerdict(ChatHistoryImportRowStatus.Invalid,
                    reason, DateTimeOffset.UnixEpoch, string.Empty));
                continue;
            }
            if (!seen.Add(row.ExternalId.Trim()))
            {
                verdicts.Add(new ChatHistoryImportVerdict(ChatHistoryImportRowStatus.Duplicate,
                    "duplicate_in_file", DateTimeOffset.UnixEpoch, string.Empty));
                continue;
            }
            if (alreadyImported(row.ExternalId.Trim()))
            {
                verdicts.Add(new ChatHistoryImportVerdict(ChatHistoryImportRowStatus.Duplicate,
                    "already_imported", DateTimeOffset.UnixEpoch, string.Empty));
                continue;
            }
            verdicts.Add(new ChatHistoryImportVerdict(
                ChatHistoryImportRowStatus.Imported,
                null,
                DateTimeOffset.Parse(row.SentAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                Hash(row.Content)));
        }
        return verdicts;
    }

    private static string? Validate(ChatHistoryImportSourceRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ExternalId) || row.ExternalId.Length > 200)
            return "invalid_external_id";
        if (string.IsNullOrWhiteSpace(row.Sender) || row.Sender.Length > 200)
            return "invalid_sender";
        if (string.IsNullOrWhiteSpace(row.Recipient) || row.Recipient.Length > 200)
            return "invalid_recipient";
        if (string.IsNullOrWhiteSpace(row.Content) || row.Content.Length > 8000)
            return "invalid_content";
        if (row.Sender.Any(char.IsControl) || row.Recipient.Any(char.IsControl) || row.ExternalId.Any(char.IsControl))
            return "control_character";
        if (!DateTimeOffset.TryParse(row.SentAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
            return "invalid_sent_at";
        return null;
    }

    private static string Hash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
