using System.Security.Cryptography;
using System.Text;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

/// <summary>
/// Validates Meta's GET webhook subscription handshake without leaking the configured verify token.
/// The caller must return the challenge only when this method succeeds.
/// </summary>
public static class WhatsAppMetaWebhookChallengeValidator
{
    public static bool TryValidate(
        string? mode,
        string? suppliedVerifyToken,
        string? expectedVerifyToken,
        string? challenge,
        out string challengeResponse)
    {
        challengeResponse = string.Empty;
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(suppliedVerifyToken) ||
            string.IsNullOrEmpty(expectedVerifyToken) ||
            string.IsNullOrEmpty(challenge) ||
            challenge.Length > 2048 ||
            challenge.Any(char.IsControl))
            return false;

        var supplied = Encoding.UTF8.GetBytes(suppliedVerifyToken);
        var expected = Encoding.UTF8.GetBytes(expectedVerifyToken);
        try
        {
            if (supplied.Length != expected.Length ||
                !CryptographicOperations.FixedTimeEquals(supplied, expected))
                return false;

            challengeResponse = challenge;
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(supplied);
            CryptographicOperations.ZeroMemory(expected);
        }
    }
}
