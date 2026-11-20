using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Saas.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;

namespace XiHan.BasicApp.Crm.Infrastructure.Repositories;

public sealed class CrmExamQuestionRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmExamQuestion>(clientResolver), ICrmExamQuestionRepository
{
    public async Task<IReadOnlyList<SysCrmExamQuestion>> GetByIdsAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return [];
        return await CreateQueryable().Where(question => ids.Contains(question.BasicId)).ToListAsync(cancellationToken);
    }
}

public sealed class CrmExamPaperRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmExamPaper>(clientResolver), ICrmExamPaperRepository;

public sealed class CrmExamAttemptRepository(ISqlSugarClientResolver clientResolver)
    : SaasRepository<SysCrmExamAttempt>(clientResolver), ICrmExamAttemptRepository
{
    public async Task<IReadOnlyList<SysCrmExamAttempt>> GetByPaperAndUserAsync(
        long paperId,
        long userId,
        CancellationToken cancellationToken = default)
        => await CreateQueryable()
            .Where(attempt => attempt.PaperId == paperId && attempt.UserId == userId)
            .OrderByDescending(attempt => attempt.CreatedTime)
            .Take(50)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SysCrmExamAttempt>> GetByUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
        => await CreateQueryable()
            .Where(attempt => attempt.UserId == userId)
            .OrderByDescending(attempt => attempt.CreatedTime)
            .Take(100)
            .ToListAsync(cancellationToken);
}
