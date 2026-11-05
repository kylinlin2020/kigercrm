using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingExtractionPersistenceContractTests
{
    [Fact]
    public void ExtractionJob_ShouldBeStrictlyTenantScoped()
        => Assert.Contains(typeof(IStrictMultiTenantEntity),
            typeof(SysCrmProspectingExtractionJob).GetInterfaces());

    [Fact]
    public void Repository_ShouldExposeDurableClaimExpiryAndCursorOperations()
    {
        var methods = typeof(ICrmProspectingExtractionJobRepository).GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("CreateSubmissionIntentAsync", methods);
        var directCreate = typeof(ICrmProspectingExtractionJobRepository)
            .GetMethod("CreateAsync");
        Assert.NotNull(directCreate);
        Assert.NotNull(Attribute.GetCustomAttribute(directCreate!, typeof(ObsoleteAttribute)));
        Assert.Contains("TryAcknowledgeSubmissionAsync", methods);
        Assert.Contains("GetByIdAsync", methods);
        Assert.Contains("ClaimDueAsync", methods);
        Assert.Contains("TryRenewLeaseAsync", methods);
        Assert.Contains("ExpireDueAsync", methods);
        Assert.Contains("TryTransitionAsync", methods);
        Assert.Contains("TryCompleteResultsAsync", methods);
        Assert.Contains("TryAdvanceCursorAsync", methods);
    }

    [Fact]
    public void ExtractionLeaseRenewal_ShouldRequireLiveTenantScopedFencedLease()
    {
        var repository = ReadRepositorySource();
        var start = repository.IndexOf("public async Task<bool> TryRenewLeaseAsync", StringComparison.Ordinal);
        var end = repository.IndexOf("public async Task<int> ExpireDueAsync", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var renewal = repository[start..end];
        Assert.Contains("lease_expires_at_utc > clock_timestamp()", renewal, StringComparison.Ordinal);
        Assert.Contains("lease_token = @leaseToken", renewal, StringComparison.Ordinal);
        Assert.Contains("fencing_version = @fencingVersion", renewal, StringComparison.Ordinal);
        Assert.Contains("tenant_id = @tenantId", renewal, StringComparison.Ordinal);
        Assert.Contains("leaseDuration > TimeSpan.FromMinutes(15)", renewal, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteProviderStatuses_ShouldNotBePersistedLifecycleStatuses()
    {
        Assert.NotEqual(
            typeof(CrmProspectingExtractionStatus),
            typeof(CrmProspectingExtractionProviderStatus));
        Assert.DoesNotContain(
            Enum.GetNames<CrmProspectingExtractionProviderStatus>(),
            name => name == nameof(CrmProspectingExtractionStatus.FetchingResults)
                || name == nameof(CrmProspectingExtractionStatus.Completed));
    }

    [Fact]
    public void ExtractionJob_ShouldCarryTenantRunFingerprintAndFencingFields()
    {
        var properties = typeof(SysCrmProspectingExtractionJob).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(SysCrmProspectingExtractionJob.TenantId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.RunId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.RequestFingerprint), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.SubmissionIdempotencyKey), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.ProviderExtractionId), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.NextPollAtUtc), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.ResultCursor), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.LeaseToken), properties);
        Assert.Contains(nameof(SysCrmProspectingExtractionJob.FencingVersion), properties);
    }
}
