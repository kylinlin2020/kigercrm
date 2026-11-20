using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>CRM customer import with field mapping, de-duplication preview and audit trail.</summary>
public interface ICrmCustomerImportAppService : IApplicationService
{
    Task<CrmCustomerImportPreviewDto> PreviewAsync(CrmCustomerImportInputDto input, CancellationToken cancellationToken = default);
    Task<CrmCustomerImportResultDto> ImportAsync(CrmCustomerImportInputDto input, CancellationToken cancellationToken = default);
}