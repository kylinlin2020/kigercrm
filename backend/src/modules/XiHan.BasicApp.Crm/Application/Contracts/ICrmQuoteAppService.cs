using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

public interface ICrmQuoteAppService : IApplicationService
{
    Task<CrmQuoteDto> CreateAsync(CrmQuoteCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmQuoteDto> ReviseAsync(long sourceQuoteId, CrmQuoteCreateDto input, CancellationToken cancellationToken = default);
    Task<CrmQuoteDto> ChangeStatusAsync(CrmQuoteStatusUpdateDto input, CancellationToken cancellationToken = default);
}
