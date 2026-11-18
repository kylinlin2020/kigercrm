using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmOutreachSequenceRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmOutreachSequence>(clientResolver), ICrmOutreachSequenceRepository
{
    public async Task<SysCrmOutreachSequence?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable().Where(item => item.Name == name).ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysCrmOutreachSequence>> GetAllAsync(CancellationToken cancellationToken = default)
        => await CreateQueryable().OrderByDescending(item => item.CreatedTime).Take(100).ToListAsync(cancellationToken);
}

public sealed class CrmOutreachEnrollmentRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmOutreachEnrollment>(clientResolver), ICrmOutreachEnrollmentRepository
{
    public async Task<SysCrmOutreachEnrollment?> GetActiveByCandidateAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        cancellationToken.ThrowIfCancellationRequested();
        var rows = await CreateQueryable().Where(item =>
                item.CandidateId == candidateId
                && (item.Status == CrmOutreachEnrollmentStatus.Active || item.Status == CrmOutreachEnrollmentStatus.Paused))
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysCrmOutreachEnrollment>> GetByCandidateAsync(long candidateId, CancellationToken cancellationToken = default)
    {
        if (candidateId <= 0) throw new ArgumentOutOfRangeException(nameof(candidateId));
        return await CreateQueryable().Where(item => item.CandidateId == candidateId)
            .OrderByDescending(item => item.CreatedTime).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SysCrmOutreachEnrollment>> GetBySequenceAsync(long sequenceId, CancellationToken cancellationToken = default)
    {
        if (sequenceId <= 0) throw new ArgumentOutOfRangeException(nameof(sequenceId));
        return await CreateQueryable().Where(item => item.SequenceId == sequenceId)
            .OrderByDescending(item => item.CreatedTime).Take(500).ToListAsync(cancellationToken);
    }
}

public sealed class CrmOutreachTouchpointRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmOutreachTouchpoint>(clientResolver), ICrmOutreachTouchpointRepository
{
    public async Task<IReadOnlyList<SysCrmOutreachTouchpoint>> GetByEnrollmentAsync(long enrollmentId, CancellationToken cancellationToken = default)
    {
        if (enrollmentId <= 0) throw new ArgumentOutOfRangeException(nameof(enrollmentId));
        return await CreateQueryable().Where(item => item.EnrollmentId == enrollmentId)
            .OrderBy(item => item.Step).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<SysCrmOutreachTouchpoint?> GetByIdForStateAsync(
        long touchpointId,
        CrmOutreachTouchpointState expected,
        CancellationToken cancellationToken = default)
    {
        if (touchpointId <= 0) throw new ArgumentOutOfRangeException(nameof(touchpointId));
        var rows = await CreateQueryable().Where(item => item.BasicId == touchpointId && item.State == expected)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }
}
