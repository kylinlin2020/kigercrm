using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Operator-reviewed business-card OCR import; no OCR engine and no automatic Lead creation.</summary>
public interface ICrmBusinessCardImportAppService : IApplicationService
{
    Task<CrmBusinessCardPreviewDto> PreviewAsync(CrmBusinessCardImportInputDto input, CancellationToken cancellationToken = default);
    Task<CrmBusinessCardImportResultDto> ImportAsync(CrmBusinessCardImportInputDto input, CancellationToken cancellationToken = default);
    Task<CrmBusinessCardRowDto> ConfirmAsync(CrmBusinessCardDecisionDto input, CancellationToken cancellationToken = default);
    Task<CrmBusinessCardRowDto> RejectAsync(CrmBusinessCardDecisionDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmBusinessCardRowDto>> GetRowsAsync(string batchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetBatchesAsync(CancellationToken cancellationToken = default);
}
