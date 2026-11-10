// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace XiHan.BasicApp.Chat.Infrastructure.Channels;

/// <summary>
/// Verifies the X-Hub-Signature-256 header used by Meta webhooks.
/// The caller must pass the exact raw request-body bytes before JSON parsing or re-serialization.
/// </summary>
public static class WhatsAppMetaWebhookSignatureValidator
{
    private const string HeaderPrefix = "sha256=";
    private const int Sha256HexLength = 64;

    /// <summary>Returns true when the header carries a valid SHA-256 HMAC of the exact raw request body.</summary>
    public static bool IsValid(ReadOnlySpan<byte> rawBody, string? signatureHeader, string? appSecret)
    {
        if (rawBody.IsEmpty ||
            string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrEmpty(appSecret) ||
            !signatureHeader.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suppliedHex = signatureHeader.AsSpan(HeaderPrefix.Length);
        if (suppliedHex.Length != Sha256HexLength)
            return false;

        byte[] suppliedDigest;
        try
        {
            suppliedDigest = Convert.FromHexString(suppliedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var secretBytes = Encoding.UTF8.GetBytes(appSecret);
        try
        {
            Span<byte> expectedDigest = stackalloc byte[32];
            if (!HMACSHA256.TryHashData(secretBytes, rawBody, expectedDigest, out var expectedLength) ||
                expectedLength != expectedDigest.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(expectedDigest, suppliedDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }
}
