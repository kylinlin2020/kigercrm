using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Api.Tests;

public sealed class CrmProspectingRunProviderExecutionRepositoryContractTests
{
    [Fact]
    public void ProviderExecution_ShouldBeStrictlyTenantScopedAndCarryImmutablePlanIdentity()
    {
        Assert.Contains(typeof(IStrictMultiTenantEntity),
            typeof(SysCrmProspectingRunProviderExecution).GetInterfaces());
        var properties = typeof(SysCrmProspectingRunProviderExecution).GetProperties()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var property in new[]
        {
            nameof(SysCrmProspectingRunProviderExecution.TenantId),
            nameof(SysCrmProspectingRunProviderExecution.RunId),
            nameof(SysCrmProspectingRunProviderExecution.PlanVersion),
            nameof(SysCrmProspectingRunProviderExecution.PlanOrdinal),
            nameof(SysCrmProspectingRunProviderExecution.ProviderCode),
            nameof(SysCrmProspectingRunProviderExecution.RequestFingerprint),
            nameof(SysCrmProspectingRunProviderExecution.CheckpointJson),
            nameof(SysCrmProspectingRunProviderExecution.CheckpointHash),
            nameof(SysCrmProspectingRunProviderExecution.FencingVersion)
        })
            Assert.Contains(property, properties);
    }

    [Fact]
    public void RepositoryContract_ShouldExposeFencedStartCheckpointAndTransitionOperations()
    {
        var methods = typeof(ICrmProspectingRunProviderExecutionRepository).GetMethods()
            .Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.GetByRunAsync), methods);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.TryStartAsync), methods);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.TrySaveCheckpointAsync), methods);
        Assert.Contains(nameof(ICrmProspectingRunProviderExecutionRepository.TryTransitionAsync), methods);
    }

    [Fact]
    public void Repository_ShouldRequireLiveRunFenceAndTransactionForCheckpointWrites()
    {
        var source = ReadRepository();
        Assert.Contains("run.lease_token = @leaseToken", source, StringComparison.Ordinal);
        Assert.Contains("run.fencing_version = @fencingVersion", source, StringComparison.Ordinal);
        Assert.Contains("run.lease_expires_at > clock_timestamp()", source, StringComparison.Ordinal);
        Assert.Contains("step.tenant_id = @tenantId", source, StringComparison.Ordinal);
        Assert.Contains("client.Ado.Transaction is null", source, StringComparison.Ordinal);
        Assert.Contains("SHA256.HashData(bytes)", source, StringComparison.Ordinal);
        Assert.Contains("MaxCheckpointBytes = 1_048_576", source, StringComparison.Ordinal);
    }

    private static string ReadRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "src")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "backend", "src", "modules",
            "XiHan.BasicApp.Crm", "Infrastructure", "Repositories",
            "CrmProspectingRunProviderExecutionRepository.cs"));
    }
}
