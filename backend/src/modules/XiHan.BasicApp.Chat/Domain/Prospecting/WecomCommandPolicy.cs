using System.Text;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;

namespace XiHan.BasicApp.Chat.Domain.Prospecting;

public sealed record WecomCommandResult(string Command, WecomCommandOutcome Outcome, string Reason, string Reply);

/// <summary>
/// Deterministic command allowlist. Unknown or malformed commands are audited as ignored and never
/// trigger an external action; the returned text is a local reply string, not a sent message.
/// </summary>
public static class WecomCommandPolicy
{
    private static readonly IReadOnlyDictionary<string, string> Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["help"] = "Supported commands: help, status, ping.",
        ["status"] = "Channel gateway is online. No external action was performed.",
        ["ping"] = "pong",
    };

    public static WecomCommandResult Evaluate(string? text, int textLengthLimit = 500)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new WecomCommandResult(string.Empty, WecomCommandOutcome.Ignored, "empty_text", string.Empty);
        if (text.Length > textLengthLimit || text.Any(char.IsControl))
            return new WecomCommandResult(string.Empty, WecomCommandOutcome.Rejected, "invalid_text", string.Empty);

        var command = text.Trim().TrimStart('/', '#').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        if (command.Length == 0 || command.Length > 80)
            return new WecomCommandResult(command, WecomCommandOutcome.Rejected, "invalid_command", string.Empty);
        if (!Replies.TryGetValue(command, out var reply))
            return new WecomCommandResult(command, WecomCommandOutcome.Ignored, "unknown_command", string.Empty);
        return new WecomCommandResult(command, WecomCommandOutcome.Replied, "ok", reply);
    }

    public static string HashReply(string reply)
        => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            Encoding.UTF8.GetBytes(reply))).ToLowerInvariant();
}
