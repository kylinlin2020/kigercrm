using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

public interface ICrmExamCertificateRepository : ISaasRepository<SysCrmExamCertificate>
{
    Task<SysCrmExamCertificate?> GetByAttemptAsync(long attemptId, CancellationToken cancellationToken = default);

    Task<SysCrmExamCertificate?> GetByNumberAsync(string certificateNo, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmExamCertificate>> GetByHolderAsync(long holderUserId, CancellationToken cancellationToken = default);
}
