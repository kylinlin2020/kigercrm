using System.Security.Cryptography;
using System.Text;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

/// <summary>
/// WeCom callback signature verification: SHA-1 over the lexicographically sorted token,
/// timestamp, nonce and (for encrypted callbacks) the message payload. No plaintext secret is
/// logged or persisted; the token comes from server configuration.
/// </summary>
public static class WecomCallbackSignatureValidator
{
    public static bool IsValid(string? token, string? timestamp, string? nonce, string? msgEncrypt, string? signature)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature))
            return false;
        var values = new[] { token, timestamp, nonce, msgEncrypt ?? string.Empty };
        Array.Sort(values, StringComparer.Ordinal);
        var computed = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Concat(values)))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}
