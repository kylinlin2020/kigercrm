using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Read-only customs-data validation, filtering and CSV export; no external data source.</summary>
public interface ICrmCustomsExportQueryService : IApplicationService
{
    Task<CrmCustomsValidationDto> ValidateAsync(CrmCustomsInputDto input, CancellationToken cancellationToken = default);
    Task<CrmCustomsFilterResultDto> FilterAsync(CrmCustomsInputDto input, CancellationToken cancellationToken = default);
    Task<CrmCustomsExportResultDto> ExportAsync(CrmCustomsInputDto input, CancellationToken cancellationToken = default);
}