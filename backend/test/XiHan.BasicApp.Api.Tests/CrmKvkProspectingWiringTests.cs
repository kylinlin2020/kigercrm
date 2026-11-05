using Xunit;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmKvkProspectingWiringTests
{
    [Fact]
    public void KvkProvider_ShouldUseTenantEncryptedCredentialAndExplicitOptInRegistration()
    {
        var root = FindRepositoryRoot();
        var provider = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmKvkProspectingProvider.cs"));
        var registration = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "backend", "src", "main", "XiHan.BasicApp.WebHost", "appsettings.json"));
        var credentials = File.ReadAllText(Path.Combine(root, "frontend", "src", "modules", "crm", "views", "crm", "prospecting", "ProviderCredentialsPanel.vue"));

        Assert.Contains("CRM.Prospecting.Provider.kvk.ApiKey", provider, StringComparison.Ordinal);
        Assert.Contains("request.Headers.Add(\"apikey\", apiKey.Trim())", provider, StringComparison.Ordinal);
        Assert.Contains("EnableKvkProvider", registration, StringComparison.Ordinal);
        Assert.Contains("CrmKvkProspectingProvider", registration, StringComparison.Ordinal);
        Assert.Contains("api.kvk.nl", registration, StringComparison.Ordinal);
        Assert.Contains("\"EnableKvkProvider\": false", settings, StringComparison.Ordinal);
        Assert.Contains("code: 'kvk'", credentials, StringComparison.Ordinal);
        Assert.Contains("ConfigType.Tenant", credentials, StringComparison.Ordinal);
        Assert.Contains("isEncrypted: true", credentials, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the KIGER CRM repository root.");
    }

    [Fact]
    public void BraveSearchProvider_ShouldUseTenantEncryptedTokenAndExplicitOptInRegistration()
    {
        var root = FindRepositoryRoot();
        var provider = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmBraveSearchProspectingProvider.cs"));
        var registration = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "backend", "src", "main", "XiHan.BasicApp.WebHost", "appsettings.json"));
        var credentials = File.ReadAllText(Path.Combine(root, "frontend", "src", "modules", "crm", "views", "crm", "prospecting", "ProviderCredentialsPanel.vue"));

        Assert.Contains("CRM.Prospecting.Provider.brave.ApiKey", provider, StringComparison.Ordinal);
        Assert.Contains("X-Subscription-Token", provider, StringComparison.Ordinal);
        Assert.Contains("EnableBraveSearchProvider", registration, StringComparison.Ordinal);
        Assert.Contains("CrmBraveSearchProspectingProvider", registration, StringComparison.Ordinal);
        Assert.Contains("api.search.brave.com", registration, StringComparison.Ordinal);
        Assert.Contains("\"EnableBraveSearchProvider\": false", settings, StringComparison.Ordinal);
        Assert.Contains("code: 'brave'", credentials, StringComparison.Ordinal);
        Assert.Contains("ConfigType.Tenant", credentials, StringComparison.Ordinal);
        Assert.Contains("isEncrypted: true", credentials, StringComparison.Ordinal);
    }

    [Fact]
    public void SerperAndSerpApi_ShouldUseTenantEncryptedKeysAndExplicitOptInRegistration()
    {
        var root = FindRepositoryRoot();
        var serper = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmSerperSearchProspectingProvider.cs"));
        var serpApi = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Infrastructure", "Prospecting", "CrmSerpApiProspectingProvider.cs"));
        var registration = File.ReadAllText(Path.Combine(root, "backend", "src", "modules", "XiHan.BasicApp.Crm", "Extensions", "ServiceCollectionExtensions.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "backend", "src", "main", "XiHan.BasicApp.WebHost", "appsettings.json"));
        var credentials = File.ReadAllText(Path.Combine(root, "frontend", "src", "modules", "crm", "views", "crm", "prospecting", "ProviderCredentialsPanel.vue"));

        Assert.Contains("CRM.Prospecting.Provider.serper.ApiKey", serper, StringComparison.Ordinal);
        Assert.Contains("X-API-KEY", serper, StringComparison.Ordinal);
        Assert.Contains("CRM.Prospecting.Provider.serpapi.ApiKey", serpApi, StringComparison.Ordinal);
        Assert.Contains("\"api_key\"", serpApi, StringComparison.Ordinal);
        Assert.Contains("EnableSerperProvider", registration, StringComparison.Ordinal);
        Assert.Contains("EnableSerpApiProvider", registration, StringComparison.Ordinal);
        Assert.Contains("google.serper.dev", registration, StringComparison.Ordinal);
        Assert.Contains("serpapi.com", registration, StringComparison.Ordinal);
        Assert.Contains("\"EnableSerperProvider\": false", settings, StringComparison.Ordinal);
        Assert.Contains("\"EnableSerpApiProvider\": false", settings, StringComparison.Ordinal);
        Assert.Contains("code: 'serper'", credentials, StringComparison.Ordinal);
        Assert.Contains("code: 'serpapi'", credentials, StringComparison.Ordinal);
        Assert.Contains("ConfigType.Tenant", credentials, StringComparison.Ordinal);
        Assert.Contains("isEncrypted: true", credentials, StringComparison.Ordinal);
    }
}
