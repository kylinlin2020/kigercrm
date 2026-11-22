using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

public interface ICrmOutreachDraftRepository : ISaasRepository<SysCrmOutreachDraft>
{
    Task<IReadOnlyList<SysCrmOutreachDraft>> GetByEnrollmentAsync(
        long enrollmentId,
        CancellationToken cancellationToken = default);

    Task<SysCrmOutreachDraft?> GetByIdForStateAsync(
        long draftId,
        CrmOutreachDraftStatus expected,
        CancellationToken cancellationToken = default);
}
