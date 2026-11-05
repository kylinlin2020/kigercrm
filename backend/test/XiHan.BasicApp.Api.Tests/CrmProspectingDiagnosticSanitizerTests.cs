using XiHan.BasicApp.Crm.Domain.Prospecting;
using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingDiagnosticSanitizerTests
{
    [Theory]
    [InlineData("key=census-secret", "key=[REDACTED]")]
    [InlineData("api_key=sam-secret", "api_key=[REDACTED]")]
    [InlineData("apikey: kvk-secret", "apikey=[REDACTED]")]
    [InlineData("X-Subscription-Token: brave-secret", "X-Subscription-Token=[REDACTED]")]
    [InlineData("Ocp-Apim-Subscription-Key: comtrade-secret", "Ocp-Apim-Subscription-Key=[REDACTED]")]
    [InlineData("subscription_key: provider-secret", "subscription_key=[REDACTED]")]
    [InlineData("client_secret=oauth-secret", "client_secret=[REDACTED]")]
    public void Sanitize_RedactsProviderCredentials(string input, string expected)
    {
        var result = CrmProspectingDiagnosticSanitizer.Sanitize(input, 500);

        Assert.Equal(expected, result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_RemovesEntireUrlQueryAndFragment()
    {
        var result = CrmProspectingDiagnosticSanitizer.Sanitize(
            "Request failed: https://api.census.gov/data?key=private-value&time=2026-09#fragment",
            500);

        Assert.Equal("Request failed: https://api.census.gov/data", result);
        Assert.DoesNotContain("private-value", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_RedactsAuthorizationAndHonorsMaximumLength()
    {
        var result = CrmProspectingDiagnosticSanitizer.Sanitize(
            "Authorization: Bearer abc.def.ghi; api_key=private-value",
            32);

        Assert.True(result.Length <= 32);
        Assert.DoesNotContain("abc.def.ghi", result, StringComparison.Ordinal);
        Assert.DoesNotContain("private-value", result, StringComparison.Ordinal);
    }
}
