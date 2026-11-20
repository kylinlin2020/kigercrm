using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Repositories;

namespace XiHan.BasicApp.Crm.Domain.Repositories;

public interface ICrmExamQuestionRepository : ISaasRepository<SysCrmExamQuestion>
{
    Task<IReadOnlyList<SysCrmExamQuestion>> GetByIdsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);
}

public interface ICrmExamPaperRepository : ISaasRepository<SysCrmExamPaper>;

public interface ICrmExamAttemptRepository : ISaasRepository<SysCrmExamAttempt>
{
    Task<IReadOnlyList<SysCrmExamAttempt>> GetByPaperAndUserAsync(long paperId, long userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SysCrmExamAttempt>> GetByUserAsync(long userId, CancellationToken cancellationToken = default);
}
