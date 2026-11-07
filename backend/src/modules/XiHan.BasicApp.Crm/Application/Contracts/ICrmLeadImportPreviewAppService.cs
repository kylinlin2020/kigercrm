using XiHan.BasicApp.Crm.Application.Importing;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Preview-only CRM lead CSV import operations.</summary>
public interface ICrmLeadImportPreviewAppService : IApplicationService
{
    Task<CrmCsvImportPreviewResult> PreviewAsync(
        CrmLeadImportPreviewRequestDto input,
        CancellationToken cancellationToken = default);
}
