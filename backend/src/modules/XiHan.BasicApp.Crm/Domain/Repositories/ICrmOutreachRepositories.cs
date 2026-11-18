using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

public interface ICrmOutreachSequenceRepository : ISaasRepository<SysCrmOutreachSequence>
{
    Task<SysCrmOutreachSequence?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SysCrmOutreachSequence>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface ICrmOutreachEnrollmentRepository : ISaasRepository<SysCrmOutreachEnrollment>
{
    Task<SysCrmOutreachEnrollment?> GetActiveByCandidateAsync(long candidateId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SysCrmOutreachEnrollment>> GetByCandidateAsync(long candidateId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SysCrmOutreachEnrollment>> GetBySequenceAsync(long sequenceId, CancellationToken cancellationToken = default);
}

public interface ICrmOutreachTouchpointRepository : ISaasRepository<SysCrmOutreachTouchpoint>
{
    Task<IReadOnlyList<SysCrmOutreachTouchpoint>> GetByEnrollmentAsync(long enrollmentId, CancellationToken cancellationToken = default);

    Task<SysCrmOutreachTouchpoint?> GetByIdForStateAsync(
        long touchpointId,
        CrmOutreachTouchpointState expected,
        CancellationToken cancellationToken = default);
}
