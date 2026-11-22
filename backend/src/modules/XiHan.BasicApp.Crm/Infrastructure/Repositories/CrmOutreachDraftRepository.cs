using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmOutreachDraftRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmOutreachDraft>(clientResolver), ICrmOutreachDraftRepository
{
    public async Task<IReadOnlyList<SysCrmOutreachDraft>> GetByEnrollmentAsync(
        long enrollmentId,
        CancellationToken cancellationToken = default)
        => await CreateQueryable()
            .Where(draft => draft.EnrollmentId == enrollmentId)
            .OrderBy(draft => draft.Step)
            .OrderBy(draft => draft.CreatedTime)
            .ToListAsync(cancellationToken);

    public async Task<SysCrmOutreachDraft?> GetByIdForStateAsync(
        long draftId,
        CrmOutreachDraftStatus expected,
        CancellationToken cancellationToken = default)
        => (await CreateQueryable()
            .Where(draft => draft.BasicId == draftId && draft.Status == expected)
            .Take(1).ToListAsync(cancellationToken)).FirstOrDefault();
}
