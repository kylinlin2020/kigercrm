using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.Framework.Application.Contracts.Services;

namespace XiHan.BasicApp.Crm.Application.Contracts;

/// <summary>
/// Read-only preview of a deterministic multi-round Super Search plan. The planner never calls
/// providers, changes Campaign/Strategy state, raises budgets or creates CRM records; execution
/// remains an explicit Run through the existing pipeline (a later milestone).
/// </summary>
public interface ICrmProspectingSuperSearchQueryService : IApplicationService
{
    Task<CrmProspectingSuperSearchPreviewDto> PreviewSuperSearchAsync(
        CrmProspectingSuperSearchPreviewInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>Map a bounded natural-language objective into reviewable planner inputs; never executes it.</summary>
    Task<CrmProspectingSuperSearchObjectiveParseResultDto> ParseSuperSearchObjectiveAsync(
        CrmProspectingSuperSearchObjectiveParseDto input,
        CancellationToken cancellationToken = default);
}
