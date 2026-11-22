using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Customer shared pool: add, claim, release, assign and recover idle claims.</summary>
public interface ICrmCustomerPoolAppService : IApplicationService
{
    Task<CrmCustomerPoolEntryDto> AddAsync(CrmCustomerPoolAddDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmCustomerPoolEntryDto>> GetAvailableAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmCustomerPoolEntryDto>> GetMineAsync(CancellationToken cancellationToken = default);
    Task<CrmCustomerPoolEntryDto> ClaimAsync(CrmCustomerPoolActionDto input, CancellationToken cancellationToken = default);
    Task<CrmCustomerPoolEntryDto> ReleaseAsync(CrmCustomerPoolActionDto input, CancellationToken cancellationToken = default);
    Task<CrmCustomerPoolEntryDto> AssignAsync(long entryId, long userId, CancellationToken cancellationToken = default);
    Task<int> RecoverIdleAsync(CancellationToken cancellationToken = default);
}
