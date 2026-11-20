using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Operator-mapped trade-document OCR import with explicit shipment creation on confirmation.</summary>
public interface ICrmTradeDocumentImportAppService : IApplicationService
{
    Task<CrmTradeDocumentPreviewDto> PreviewAsync(CrmTradeDocumentImportInputDto input, CancellationToken cancellationToken = default);
    Task<CrmTradeDocumentImportResultDto> ImportAsync(CrmTradeDocumentImportInputDto input, CancellationToken cancellationToken = default);
    Task<CrmTradeDocumentRowDto> ConfirmAsync(CrmTradeDocumentConfirmDto input, CancellationToken cancellationToken = default);
    Task<CrmTradeDocumentRowDto> RejectAsync(CrmTradeDocumentDecisionDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmTradeDocumentRowDto>> GetRowsAsync(string batchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetBatchesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmTradeDocumentOrderLineDto>> GetOrderLinesAsync(long salesOrderId, CancellationToken cancellationToken = default);
}
