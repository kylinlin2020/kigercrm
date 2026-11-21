using System.Text.Json;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Security.Users;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Sales training exams. The correct answer and explanation never appear in candidate-facing
/// DTOs; results carry only per-question verdicts and the final score.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "Exam")]
public sealed class CrmExamAppService(
    ICrmExamQuestionRepository questions,
    ICrmExamPaperRepository papers,
    ICrmExamAttemptRepository attempts,
    ICurrentUser currentUser)
    : CrmApplicationService, ICrmExamAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmExamQuestionDto> CreateQuestionAsync(
        CrmExamQuestionCreateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Prompt);
        var options = input.Options.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (options.Length < 2) throw new ArgumentException("An exam question needs at least two options.", nameof(input));
        if (input.Points < 1) throw new ArgumentOutOfRangeException(nameof(input.Points));
        var type = (CrmExamQuestionType)input.Type;
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(input.Type));
        var correct = type == CrmExamQuestionType.MultipleChoice && input.CorrectAnswers.Length > 0
            ? input.CorrectAnswers
            : [input.CorrectAnswer];
        var correctAnswers = CrmExamPolicy.ParseCorrectAnswers(type, string.Join(',', correct));
        CrmExamPolicy.ValidateQuestion(type, options, correctAnswers);
        var created = await questions.AddAsync(new SysCrmExamQuestion
        {
            Prompt = input.Prompt.Trim(),
            Type = type,
            Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim(),
            OptionsJson = JsonSerializer.Serialize(options, JsonOptions),
            CorrectAnswer = string.Join(',', correctAnswers),
            Explanation = input.Explanation?.Trim(),
            Points = input.Points
        }, cancellationToken);
        return ToQuestionDto(created);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmExamQuestionDto>> GetQuestionsAsync(CancellationToken cancellationToken = default)
        => (await questions.GetAllAsync(cancellationToken)).Select(ToQuestionDto).ToArray();

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmExamQuestionDto>> GetQuestionsByCategoryAsync(
        string? category,
        CancellationToken cancellationToken = default)
    {
        var rows = await questions.GetAllAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(category)) return rows.Select(ToQuestionDto).ToArray();
        return rows.Where(item => string.Equals(item.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(ToQuestionDto).ToArray();
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        => (await questions.GetAllAsync(cancellationToken))
            .Where(item => !string.IsNullOrWhiteSpace(item.Category))
            .Select(item => item.Category!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Create)]
    public async Task<CrmExamPaperDto> CreatePaperAsync(
        CrmExamPaperCreateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        var rules = CrmExamSelectionPolicy.ParseRules(
            input.SelectionRules.Length == 0 ? null : JsonSerializer.Serialize(input.SelectionRules, JsonOptions));
        var allQuestions = await questions.GetAllAsync(cancellationToken);
        var ids = rules.Count > 0
            ? CrmExamSelectionPolicy.Select(rules, allQuestions, input.RandomizeSelection, input.SelectionSeed)
            : CrmExamPolicy.ParseQuestionIds(JsonSerializer.Serialize(input.QuestionIds, JsonOptions));
        var bank = await questions.GetByIdsAsync(ids, cancellationToken);
        if (bank.Count != ids.Count)
            throw new InvalidOperationException("Some exam questions do not exist in this tenant.");
        var totalScore = bank.Sum(question => Math.Max(0, question.Points));
        CrmExamPolicy.ValidatePaper(input.DurationMinutes, input.PassScore, input.MaxAttempts, totalScore);
        var created = await papers.AddAsync(new SysCrmExamPaper
        {
            Name = input.Name.Trim(),
            QuestionIdsJson = JsonSerializer.Serialize(ids, JsonOptions),
            SelectionRulesJson = rules.Count == 0 ? null : JsonSerializer.Serialize(rules, JsonOptions),
            RandomizeSelection = input.RandomizeSelection,
            SelectionSeed = input.SelectionSeed,
            DurationMinutes = input.DurationMinutes,
            PassScore = input.PassScore,
            MaxAttempts = input.MaxAttempts,
            Status = CrmExamPaperStatus.Draft
        }, cancellationToken);
        return ToPaperDto(created, totalScore);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmExamPaperDto> PublishPaperAsync(long paperId, CancellationToken cancellationToken = default)
    {
        var paper = await papers.GetByIdAsync(paperId, cancellationToken)
            ?? throw new InvalidOperationException("Exam paper does not exist.");
        if (paper.Status != CrmExamPaperStatus.Draft)
            throw new InvalidOperationException("Only draft papers can be published.");
        paper.Status = CrmExamPaperStatus.Published;
        var bank = await questions.GetByIdsAsync(CrmExamPolicy.ParseQuestionIds(paper.QuestionIdsJson), cancellationToken);
        return ToPaperDto(await papers.UpdateAsync(paper, cancellationToken), bank.Sum(item => Math.Max(0, item.Points)));
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmExamPaperDto>> GetPapersAsync(CancellationToken cancellationToken = default)
    {
        var rows = await papers.GetAllAsync(cancellationToken);
        var result = new List<CrmExamPaperDto>(rows.Count);
        foreach (var row in rows)
        {
            var bank = await questions.GetByIdsAsync(CrmExamPolicy.ParseQuestionIds(row.QuestionIdsJson), cancellationToken);
            result.Add(ToPaperDto(row, bank.Sum(item => Math.Max(0, item.Points))));
        }
        return result;
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmExamAttemptDto> StartAsync(CrmExamStartDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var userId = RequireUserId();
        var paper = await papers.GetByIdAsync(input.PaperId, cancellationToken)
            ?? throw new InvalidOperationException("Exam paper does not exist.");
        var previous = await attempts.GetByPaperAndUserAsync(input.PaperId, userId, cancellationToken);
        var gate = CrmExamPolicy.CanStart(
            paper.Status,
            previous.Count,
            paper.MaxAttempts,
            DateTimeOffset.UtcNow,
            previous.FirstOrDefault()?.StartedAtUtc);
        if (!gate.Allowed) throw new InvalidOperationException($"Exam cannot be started: {gate.Reason}.");
        var attempt = await attempts.AddAsync(new SysCrmExamAttempt
        {
            PaperId = paper.BasicId,
            UserId = userId,
            Status = CrmExamAttemptStatus.InProgress,
            StartedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        return await ToAttemptDtoAsync(attempt, paper, [], []);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmExamAttemptDto> SubmitAsync(CrmExamSubmissionDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var userId = RequireUserId();
        var attempt = await attempts.GetByIdAsync(input.AttemptId, cancellationToken)
            ?? throw new InvalidOperationException("Exam attempt does not exist.");
        if (attempt.UserId != userId) throw new InvalidOperationException("This attempt belongs to another user.");
        if (attempt.Status != CrmExamAttemptStatus.InProgress)
            throw new InvalidOperationException("Exam attempt is already submitted.");
        var paper = await papers.GetByIdAsync(attempt.PaperId, cancellationToken)
            ?? throw new InvalidOperationException("Exam paper does not exist.");
        var now = DateTimeOffset.UtcNow;
        var answers = input.Answers.Where(pair => pair.Key > 0 && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());
        if (CrmExamPolicy.IsTimeout(attempt.StartedAtUtc, now, paper.DurationMinutes))
        {
            attempt.Status = CrmExamAttemptStatus.Failed;
            attempt.SubmittedAtUtc = now;
            attempt.AnswersJson = "{}";
            await attempts.UpdateAsync(attempt, cancellationToken);
            return await ToAttemptDtoAsync(attempt, paper, [], []);
        }
        var bank = await questions.GetByIdsAsync(CrmExamPolicy.ParseQuestionIds(paper.QuestionIdsJson), cancellationToken);
        var grade = CrmExamPolicy.Grade(answers, bank);
        attempt.Status = grade.Score >= paper.PassScore ? CrmExamAttemptStatus.Passed : CrmExamAttemptStatus.Failed;
        attempt.SubmittedAtUtc = now;
        attempt.AnswersJson = JsonSerializer.Serialize(answers, JsonOptions);
        attempt.Score = grade.Score;
        attempt.TotalScore = grade.TotalScore;
        var updated = await attempts.UpdateAsync(attempt, cancellationToken);
        return await ToAttemptDtoAsync(
            updated,
            paper,
            bank.Select(ToCandidateQuestionDto).ToArray(),
            grade.Answers.Select(verdict => new CrmExamAnswerVerdictDto
            {
                QuestionId = verdict.QuestionId,
                Correct = verdict.Correct
            }).ToArray());
    }

    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<IReadOnlyList<CrmExamAttemptDto>> GetMyAttemptsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await attempts.GetByUserAsync(RequireUserId(), cancellationToken);
        var result = new List<CrmExamAttemptDto>(rows.Count);
        foreach (var row in rows)
        {
            var paper = await papers.GetByIdAsync(row.PaperId, cancellationToken);
            if (paper is null) continue;
            result.Add(await ToAttemptDtoAsync(row, paper, [], []));
        }
        return result;
    }

    private long RequireUserId()
        => currentUser.UserId ?? throw new InvalidOperationException("An authenticated user is required to take an exam.");

    private async Task<CrmExamAttemptDto> ToAttemptDtoAsync(
        SysCrmExamAttempt attempt,
        SysCrmExamPaper paper,
        IReadOnlyList<CrmExamCandidateQuestionDto> candidateQuestions,
        IReadOnlyList<CrmExamAnswerVerdictDto> verdicts)
    {
        var effectiveQuestions = candidateQuestions.Count > 0
            ? candidateQuestions
            : (await questions.GetByIdsAsync(CrmExamPolicy.ParseQuestionIds(paper.QuestionIdsJson)))
                .Select(ToCandidateQuestionDto).ToArray();
        return new CrmExamAttemptDto
        {
            BasicId = attempt.BasicId,
            PaperId = attempt.PaperId,
            PaperName = paper.Name,
            Status = (int)attempt.Status,
            StartedAtUtc = attempt.StartedAtUtc,
            SubmittedAtUtc = attempt.SubmittedAtUtc,
            Score = attempt.Score,
            TotalScore = attempt.TotalScore,
            Questions = effectiveQuestions,
            Verdicts = verdicts
        };
    }

    private static CrmExamQuestionDto ToQuestionDto(SysCrmExamQuestion entity) => new()
    {
        BasicId = entity.BasicId,
        Prompt = entity.Prompt,
        Type = (int)entity.Type,
        Category = entity.Category,
        Options = JsonSerializer.Deserialize<string[]>(entity.OptionsJson, JsonOptions) ?? [],
        CorrectAnswer = entity.CorrectAnswer,
        CorrectAnswers = ToOriginalAnswers(entity),
        Explanation = entity.Explanation,
        Points = entity.Points
    };

    private static string[] ToOriginalAnswers(SysCrmExamQuestion entity)
    {
        var options = JsonSerializer.Deserialize<string[]>(entity.OptionsJson, JsonOptions) ?? [];
        var normalized = CrmExamPolicy.ParseCorrectAnswers(entity.Type, entity.CorrectAnswer);
        return normalized
            .Select(key => options.FirstOrDefault(option => string.Equals(option.Trim(), key, StringComparison.OrdinalIgnoreCase)) ?? key)
            .ToArray();
    }
    private static CrmExamCandidateQuestionDto ToCandidateQuestionDto(SysCrmExamQuestion entity) => new()
    {
        BasicId = entity.BasicId,
        Prompt = entity.Prompt,
        Type = (int)entity.Type,
        Category = entity.Category,
        Options = JsonSerializer.Deserialize<string[]>(entity.OptionsJson, JsonOptions) ?? [],
        Points = entity.Points
    };

    private static CrmExamPaperDto ToPaperDto(SysCrmExamPaper entity, int totalScore) => new()
    {
        BasicId = entity.BasicId,
        Name = entity.Name,
        QuestionCount = CrmExamPolicy.ParseQuestionIds(entity.QuestionIdsJson).Count,
        SelectionRules = CrmExamSelectionPolicy.ParseRules(entity.SelectionRulesJson)
            .Select(rule => new CrmExamSelectionRuleDto { Category = rule.Category, Count = rule.Count }).ToArray(),
        RandomizeSelection = entity.RandomizeSelection,
        SelectionSeed = entity.SelectionSeed,
        DurationMinutes = entity.DurationMinutes,
        PassScore = entity.PassScore,
        MaxAttempts = entity.MaxAttempts,
        TotalScore = totalScore,
        Status = (int)entity.Status
    };
}
