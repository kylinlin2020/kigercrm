using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmExamCertificateRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmExamCertificate>(clientResolver), ICrmExamCertificateRepository
{
    public async Task<SysCrmExamCertificate?> GetByAttemptAsync(
        long attemptId,
        CancellationToken cancellationToken = default)
        => (await CreateQueryable().Where(item => item.AttemptId == attemptId)
            .Take(1).ToListAsync(cancellationToken)).FirstOrDefault();

    public async Task<SysCrmExamCertificate?> GetByNumberAsync(
        string certificateNo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateNo);
        return (await CreateQueryable().Where(item => item.CertificateNo == certificateNo.Trim())
            .Take(1).ToListAsync(cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<SysCrmExamCertificate>> GetByHolderAsync(
        long holderUserId,
        CancellationToken cancellationToken = default)
        => await CreateQueryable().Where(item => item.HolderUserId == holderUserId)
            .OrderByDescending(item => item.CreatedTime)
            .Take(100)
            .ToListAsync(cancellationToken);
}
