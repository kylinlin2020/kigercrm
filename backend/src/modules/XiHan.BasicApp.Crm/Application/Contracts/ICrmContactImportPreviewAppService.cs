using XiHan.BasicApp.Crm.Application.Importing;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmContactImportPreviewAppService : IApplicationService
{
    Task<CrmCsvImportPreviewResult> PreviewAsync(CrmCsvImportPreviewRequestDto input, CancellationToken cancellationToken = default);
}
