using System.Security.Cryptography;
using System.Text.Json;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Computes a response-content digest for audit comparison. The serialized response is never
/// returned or persisted by this helper; callers store only the SHA-256 digest.
/// </summary>
public static class CrmProspectingResponseFingerprint
{
    public static string Create<T>(T response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
