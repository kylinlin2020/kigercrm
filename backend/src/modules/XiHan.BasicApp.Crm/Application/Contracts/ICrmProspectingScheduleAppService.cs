using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>Approval lifecycle of recurring prospecting schedules. Dispatch is a worker concern.</summary>
public interface ICrmProspectingScheduleAppService : IApplicationService
{
    Task<CrmProspectingScheduleDto> CreateScheduleAsync(CrmProspectingScheduleCreateDto input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CrmProspectingScheduleDto>> GetSchedulesAsync(long campaignId, CancellationToken cancellationToken = default);
    Task<CrmProspectingScheduleDto> SubmitScheduleForApprovalAsync(long scheduleId, CancellationToken cancellationToken = default);
    Task<CrmProspectingScheduleDto> DecideScheduleAsync(CrmProspectingScheduleDecisionDto input, CancellationToken cancellationToken = default);
    Task<CrmProspectingScheduleDto> PauseScheduleAsync(long scheduleId, CancellationToken cancellationToken = default);
    Task<CrmProspectingScheduleDto> ResumeScheduleAsync(long scheduleId, CancellationToken cancellationToken = default);
    Task<CrmProspectingScheduleDto> CancelScheduleAsync(long scheduleId, CancellationToken cancellationToken = default);
}
