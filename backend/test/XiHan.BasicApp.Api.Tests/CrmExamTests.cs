// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Training exams: deterministic policy plus a full question→paper→attempt lifecycle.</summary>
public sealed class CrmExamTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-crm-exam-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly CrmExamAppService _service;

    public CrmExamTests()
    {
        _tenantScope = _tenant.Change(7);
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        var tenant = _tenant;
        _db = new SqlSugarScope(
            new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = _connectionString,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            },
            db =>
            {
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId() == entity.TenantId);
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                    // SQLite stores DateTimeOffset as wall-clock text and reads it back in the local
                    // zone; normalising to local here preserves the instant (PostgreSQL timestamptz
                    // does not need this).
                    if (entityInfo.EntityValue is SysCrmExamAttempt examAttempt)
                    {
                        examAttempt.StartedAtUtc = examAttempt.StartedAtUtc.ToLocalTime();
                        if (examAttempt.SubmittedAtUtc.HasValue)
                            examAttempt.SubmittedAtUtc = examAttempt.SubmittedAtUtc.Value.ToLocalTime();
                    }
                };
            });
        _db.CodeFirst.InitTables(
            typeof(SysCrmExamQuestion),
            typeof(SysCrmExamPaper),
            typeof(SysCrmExamAttempt));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(value => value.UserId).Returns(42);
        _service = new CrmExamAppService(
            new CrmExamQuestionRepository(_resolver.Object),
            new CrmExamPaperRepository(_resolver.Object),
            new CrmExamAttemptRepository(_resolver.Object),
            user.Object);
    }

    [Fact]
    public void Policy_ShouldValidatePaperTimingAndGrading()
    {
        var ids = CrmExamPolicy.ParseQuestionIds("[10,11,12]");
        Assert.Equal(3, ids.Count);
        Assert.Throws<ArgumentException>(() => CrmExamPolicy.ParseQuestionIds("[10,10]"));
        Assert.Throws<ArgumentException>(() => CrmExamPolicy.ParseQuestionIds($"[{string.Join(',', Enumerable.Range(1, 51))}]"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrmExamPolicy.ValidatePaper(181, 60, 3, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrmExamPolicy.ValidatePaper(30, 101, 3, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrmExamPolicy.ValidatePaper(30, 60, 11, 100));
        var now = DateTimeOffset.UtcNow;
        Assert.False(CrmExamPolicy.CanStart(CrmExamPaperStatus.Draft, 0, 3, now, null).Allowed);
        Assert.False(CrmExamPolicy.CanStart(CrmExamPaperStatus.Published, 3, 3, now, null).Allowed);
        Assert.False(CrmExamPolicy.CanStart(CrmExamPaperStatus.Published, 1, 3, now, now.AddSeconds(-5)).Allowed);
        Assert.True(CrmExamPolicy.CanStart(CrmExamPaperStatus.Published, 1, 3, now, now.AddMinutes(-2)).Allowed);
        Assert.True(CrmExamPolicy.IsTimeout(now.AddMinutes(-31), now, 30));
        Assert.False(CrmExamPolicy.IsTimeout(now.AddMinutes(-29), now, 30));

        var q1 = MakeQuestion(10, "A", 60);
        var q2 = MakeQuestion(11, "B", 40);
        var grade = CrmExamPolicy.Grade(new Dictionary<long, string> { [10] = "a", [11] = "A" }, [q1, q2]);
        Assert.Equal(60, grade.Score);
        Assert.Equal(100, grade.TotalScore);
        Assert.True(grade.Answers[0].Correct);
        Assert.False(grade.Answers[1].Correct);
    }

    [Fact]
    public void MultiChoice_ShouldCompareAnswerSetsOrderIndependently()
    {
        var question = MakeQuestion(20, "a,b", 40);
        typeof(SysCrmExamQuestion).GetProperty(nameof(SysCrmExamQuestion.Type))!.SetValue(
            question, CrmExamQuestionType.MultipleChoice);
        var correct = CrmExamPolicy.ParseCorrectAnswers(CrmExamQuestionType.MultipleChoice, "A, B");
        Assert.Equal(new[] { "a", "b" }, correct);
        Assert.True(CrmExamPolicy.IsAnswerCorrect(CrmExamQuestionType.MultipleChoice, "A,B", "B,A"));
        Assert.False(CrmExamPolicy.IsAnswerCorrect(CrmExamQuestionType.MultipleChoice, "A,B", "A"));
        Assert.False(CrmExamPolicy.IsAnswerCorrect(CrmExamQuestionType.MultipleChoice, "A,B", "A,B,C"));
        Assert.Throws<ArgumentException>(() => CrmExamPolicy.ValidateQuestion(
            CrmExamQuestionType.MultipleChoice, new[] { "A", "B", "C" }, new[] { "a" }));
        CrmExamPolicy.ValidateQuestion(
            CrmExamQuestionType.MultipleChoice, new[] { "A", "B", "C" }, new[] { "a", "c" });
        var grade = CrmExamPolicy.Grade(new Dictionary<long, string> { [20] = "b,a" }, [question]);
        Assert.Equal(40, grade.Score);
        Assert.True(grade.Answers[0].Correct);
        Assert.False(CrmExamPolicy.IsAnswerCorrect(CrmExamQuestionType.SingleChoice, "A", "A,B"));
    }
    [Fact]
    public async Task CategorySelection_ShouldFreezeIdsAtCreation()
    {
        var ids = new List<long>();
        foreach (var category in new[] { "Product", "Product", "Compliance", "Compliance" })
        {
            var created = await _service.CreateQuestionAsync(new CrmExamQuestionCreateDto
            {
                Prompt = $"{category} question {ids.Count}",
                Type = (int)CrmExamQuestionType.SingleChoice,
                Category = category,
                Options = ["A", "B"],
                CorrectAnswer = "A",
                Points = 10
            });
            ids.Add(created.BasicId);
        }

        Assert.Equal(new[] { "Compliance", "Product" }, await _service.GetCategoriesAsync());
        Assert.Equal(2, (await _service.GetQuestionsByCategoryAsync("product")).Count);
        Assert.Empty(await _service.GetQuestionsByCategoryAsync("missing"));

        var paper = await _service.CreatePaperAsync(new CrmExamPaperCreateDto
        {
            Name = "Selected exam",
            DurationMinutes = 30,
            PassScore = 20,
            MaxAttempts = 2,
            SelectionRules =
            [
                new CrmExamSelectionRuleDto { Category = "Compliance", Count = 1 },
                new CrmExamSelectionRuleDto { Category = "Product", Count = 1 }
            ]
        });
        Assert.Equal(2, paper.QuestionCount);
        Assert.Equal(2, paper.SelectionRules.Length);
        Assert.Equal(new[] { "Compliance", "Product" }, paper.SelectionRules.Select(rule => rule.Category));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreatePaperAsync(new CrmExamPaperCreateDto
        {
            Name = "Too many",
            DurationMinutes = 30,
            PassScore = 10,
            SelectionRules = [new CrmExamSelectionRuleDto { Category = "Compliance", Count = 5 }]
        }));
        Assert.Throws<ArgumentException>(() => CrmExamSelectionPolicy.ParseRules(
            "[{\"category\":\"A\",\"count\":1},{\"category\":\"a\",\"count\":1}]"));
        Assert.Throws<ArgumentOutOfRangeException>(() => CrmExamSelectionPolicy.ParseRules(
            "[{\"category\":\"A\",\"count\":51}]"));
    }

    [Fact]
    public async Task RandomSelection_ShouldBeSeededReproducibleAndDifferFromSequential()
    {
        var rules = new[] { new CrmExamSelectionRule("Bank", 4) };
        var bank = Enumerable.Range(1, 12)
            .Select(id => MakeQuestion(id, "A", 10))
            .ToArray();
        foreach (var question in bank)
            typeof(SysCrmExamQuestion).GetProperty(nameof(SysCrmExamQuestion.Category))!.SetValue(question, "Bank");

        var sequential = CrmExamSelectionPolicy.Select(rules, bank);
        var randomA = CrmExamSelectionPolicy.Select(rules, bank, randomize: true, seed: 42);
        var randomB = CrmExamSelectionPolicy.Select(rules, bank, randomize: true, seed: 42);
        var randomOther = CrmExamSelectionPolicy.Select(rules, bank, randomize: true, seed: 43);
        Assert.Equal(12, bank.Length);
        Assert.Equal(4, randomA.Count);
        Assert.Equal(randomA.OrderBy(id => id), randomB.OrderBy(id => id)); // same 4 ids from same seed
        Assert.Equal(randomA, randomB); // reproducible exact order
        Assert.NotEqual(sequential, randomA); // random draw is not simply the first-N sequence
        Assert.NotEqual(randomA, randomOther); // different seed -> different draw
        Assert.Throws<ArgumentException>(() => CrmExamSelectionPolicy.Select(
            rules, bank, randomize: true, seed: null));
    }

    [Fact]
    public async Task ExamFlow_ShouldPublishGradeAndHideAnswerKey()
    {
        var q1 = await _service.CreateQuestionAsync(new CrmExamQuestionCreateDto
        {
            Prompt = "Which option?",
            Options = ["A", "B"],
            CorrectAnswer = "A",
            Points = 60
        });
        var q2 = await _service.CreateQuestionAsync(new CrmExamQuestionCreateDto
        {
            Prompt = "Second?",
            Options = ["A", "B"],
            CorrectAnswer = "B",
            Points = 40
        });
        var listed = await _service.GetQuestionsAsync();
        Assert.Equal(2, listed.Count);
        // H1 security: list endpoints must never ship the answer key to the browser.
        var listedPayload = System.Text.Json.JsonSerializer.Serialize(listed);
        Assert.DoesNotContain("CorrectAnswer", listedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("CorrectAnswers", listedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("Explanation", listedPayload, StringComparison.Ordinal);
        var byCategory = await _service.GetQuestionsByCategoryAsync("product");
        var byCategoryPayload = System.Text.Json.JsonSerializer.Serialize(byCategory);
        Assert.DoesNotContain("CorrectAnswer", byCategoryPayload, StringComparison.Ordinal);

        var paper = await _service.CreatePaperAsync(new CrmExamPaperCreateDto
        {
            Name = "Sales basics",
            QuestionIds = [q1.BasicId, q2.BasicId],
            DurationMinutes = 30,
            PassScore = 60,
            MaxAttempts = 2
        });
        Assert.Equal((int)CrmExamPaperStatus.Draft, paper.Status);
        Assert.Equal(100, paper.TotalScore);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.StartAsync(new CrmExamStartDto { PaperId = paper.BasicId }));
        var published = await _service.PublishPaperAsync(paper.BasicId);
        Assert.Equal((int)CrmExamPaperStatus.Published, published.Status);

        var attempt = await _service.StartAsync(new CrmExamStartDto { PaperId = paper.BasicId });
        Assert.Equal((int)CrmExamAttemptStatus.InProgress, attempt.Status);
        var candidatePayload = System.Text.Json.JsonSerializer.Serialize(attempt);
        Assert.DoesNotContain("CorrectAnswer", candidatePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("Explanation", candidatePayload, StringComparison.Ordinal);
        var submitted = await _service.SubmitAsync(new CrmExamSubmissionDto
        {
            AttemptId = attempt.BasicId,
            Answers = new Dictionary<long, string> { [q1.BasicId] = "A", [q2.BasicId] = "A" }
        });

        Assert.Equal((int)CrmExamAttemptStatus.Passed, submitted.Status);
        Assert.Equal(60, submitted.Score);
        Assert.Equal(2, submitted.Verdicts.Count);
        Assert.True(submitted.Verdicts.First(v => v.QuestionId == q1.BasicId).Correct);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitAsync(new CrmExamSubmissionDto { AttemptId = attempt.BasicId }));
        Assert.Single(await _service.GetMyAttemptsAsync());
    }

    [Fact]
    public async Task TimeoutAndRetryLimit_ShouldFailClosed()
    {
        var question = await _service.CreateQuestionAsync(new CrmExamQuestionCreateDto
        {
            Prompt = "Timeout?",
            Options = ["A", "B"],
            CorrectAnswer = "A"
        });
        var paper = await _service.PublishPaperAsync((await _service.CreatePaperAsync(new CrmExamPaperCreateDto
        {
            Name = "Timeout exam",
            QuestionIds = [question.BasicId],
            DurationMinutes = 1,
            PassScore = 1,
            MaxAttempts = 1
        })).BasicId);

        var attempt = await _service.StartAsync(new CrmExamStartDto { PaperId = paper.BasicId });
        await _db.Updateable<SysCrmExamAttempt>()
            .SetColumns(item => item.StartedAtUtc == DateTimeOffset.UtcNow.AddMinutes(-5))
            .Where(item => item.BasicId == attempt.BasicId)
            .ExecuteCommandAsync();
        var timedOut = await _service.SubmitAsync(new CrmExamSubmissionDto
        {
            AttemptId = attempt.BasicId,
            Answers = new Dictionary<long, string> { [question.BasicId] = "A" }
        });
        Assert.Equal((int)CrmExamAttemptStatus.Failed, timedOut.Status);
        Assert.Equal(0, timedOut.Score);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.StartAsync(new CrmExamStartDto { PaperId = paper.BasicId }));
    }

    [Fact]
    public async Task MultiChoiceFlow_ShouldCreateGradeAndHideAnswerKey()
    {
        var created = await _service.CreateQuestionAsync(new CrmExamQuestionCreateDto
        {
            Prompt = "Select all that apply",
            Type = (int)CrmExamQuestionType.MultipleChoice,
            Options = ["A", "B", "C"],
            CorrectAnswers = ["A", "C"],
            Points = 50
        });
        Assert.Equal((int)CrmExamQuestionType.MultipleChoice, created.Type);
        Assert.Equal(2, created.CorrectAnswers.Length);
        Assert.Contains("A", created.CorrectAnswers);
        Assert.Contains("C", created.CorrectAnswers);

        var paper = await _service.PublishPaperAsync((await _service.CreatePaperAsync(new CrmExamPaperCreateDto
        {
            Name = "Multi-choice exam",
            QuestionIds = [created.BasicId],
            DurationMinutes = 30,
            PassScore = 50,
            MaxAttempts = 2
        })).BasicId);
        var attempt = await _service.StartAsync(new CrmExamStartDto { PaperId = paper.BasicId });
        var candidatePayload = System.Text.Json.JsonSerializer.Serialize(attempt);
        Assert.DoesNotContain("CorrectAnswer", candidatePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("CorrectAnswers", candidatePayload, StringComparison.Ordinal);

        var submitted = await _service.SubmitAsync(new CrmExamSubmissionDto
        {
            AttemptId = attempt.BasicId,
            Answers = new Dictionary<long, string> { [created.BasicId] = "C,A" }
        });
        Assert.Equal((int)CrmExamAttemptStatus.Passed, submitted.Status);
        Assert.Equal(50, submitted.Score);
        Assert.True(submitted.Verdicts[0].Correct);
    }
    [Fact]
    public void Contract_ShouldGuardPermissionsAndCandidateSurface()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmExamAppService.cs");
        var candidate = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "Dtos", "CrmExamDtos.cs");
        var candidateBlock = candidate.Substring(
            candidate.IndexOf("class CrmExamCandidateQuestionDto", StringComparison.Ordinal),
            candidate.IndexOf("class CrmExamPaperCreateDto", StringComparison.Ordinal));
        Assert.Contains("[DynamicApi(Group = \"BasicApp.Crm\"", service, StringComparison.Ordinal);
        Assert.Contains("Tag = \"Exam\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CorrectAnswer", candidateBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Explanation", candidateBlock, StringComparison.Ordinal);
    }

    private static SysCrmExamQuestion MakeQuestion(long id, string answer, int points)
    {
        var question = new SysCrmExamQuestion { CorrectAnswer = answer, Points = points };
        typeof(SysCrmExamQuestion).GetProperty(nameof(SysCrmExamQuestion.BasicId))!.SetValue(question, id);
        return question;
    }

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}

