using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>
/// Exposes the provider catalog as a read-only Dynamic API endpoint.
/// Static metadata is merged with the DI registry so the UI can honestly distinguish
/// "adapter exists in source" from "adapter is enabled and executable on this server".
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Prospecting")]
public sealed class CrmProspectingProviderCatalogQueryService(CrmProspectingProviderRegistry registry)
    : CrmApplicationService, ICrmProspectingProviderCatalogQueryService
{
    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public Task<CrmProspectingProviderCatalogDto> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var providers = CrmProspectingProviderCatalog.All
            .Select(definition =>
            {
                // The durable Run worker executes only synchronous adapters. An asynchronous-only
                // registration is reported separately instead of being advertised as usable.
                var capability = registry.Synchronous.ContainsKey(definition.Code)
                    ? "synchronous"
                    : registry.Asynchronous.ContainsKey(definition.Code)
                        ? "asynchronous"
                        : "unavailable";

                return new CrmProspectingProviderCatalogItemDto
                {
                    Code = definition.Code,
                    Label = definition.Label,
                    Group = definition.Group,
                    NeedsKey = definition.NeedsKey,
                    KeyOptional = definition.KeyOptional,
                    ConfigKey = definition.ConfigKey,
                    Note = definition.Note,
                    IsRegistered = capability != "unavailable",
                    IsExecutable = capability == "synchronous",
                    Capability = capability,
                };
            })
            .ToArray();

        return Task.FromResult(new CrmProspectingProviderCatalogDto
        {
            Providers = providers,
            TotalCount = providers.Length,
            RegisteredCount = providers.Count(item => item.IsRegistered),
            ExecutableCount = providers.Count(item => item.IsExecutable),
            AsynchronousCount = providers.Count(item => item.Capability == "asynchronous"),
        });
    }
}
