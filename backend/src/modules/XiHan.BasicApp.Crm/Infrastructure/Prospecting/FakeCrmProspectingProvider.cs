using XiHan.BasicApp.Crm.Domain.Prospecting;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Deterministic local smoke-test provider. It performs no network requests and returns synthetic .example data.
/// Never enable it as a production data source.
/// </summary>
public sealed class FakeCrmProspectingProvider : ICrmProspectingProvider
{
    public string ProviderCode => "fake";

    public Task<CrmProspectingProviderResult> SearchAsync(
        CrmProspectingProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var seed = request.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IReadOnlyList<CrmProspectingRawRecord> records =
        [
            new(
                $"fake-{seed}-001",
                "fake://prospecting",
                "Generator sourcing inquiry",
                "Example Power Systems",
                "https://example.com",
                "Alex Example",
                "alex@example.com",
                "Synthetic smoke-test result; not a real prospect."),
            new(
                $"fake-{seed}-002",
                "fake://prospecting",
                "Water pump distributor opportunity",
                "Example Industrial Supply",
                "https://example.org",
                "Taylor Example",
                "taylor@example.org",
                "Synthetic smoke-test result; not a real prospect.")
        ];

        return Task.FromResult(new CrmProspectingProviderResult(records, 0, []));
    }
}
