using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Read-only provider capability and runtime registration catalog.</summary>
public interface ICrmProspectingProviderCatalogQueryService : IApplicationService
{
    Task<CrmProspectingProviderCatalogDto> GetProvidersAsync(CancellationToken cancellationToken = default);
}
