using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmExamGradeResult(int Score, int TotalScore, IReadOnlyList<CrmExamAnswerVerdict> Answers);
public sealed record CrmExamAnswerVerdict(long QuestionId, bool Correct);

/// <summary>
/// Deterministic exam rules. Correct answers are only evaluated server-side; candidate-facing
/// results contain verdicts and scores, never the answer key.
/// </summary>
public static class CrmExamPolicy
{
    public const int MaximumQuestions = 50;
    public static readonly TimeSpan MinimumRetryGap = TimeSpan.FromMinutes(1);

    public static IReadOnlyList<long> ParseQuestionIds(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Exam question ids must be a JSON array.");
        var ids = document.RootElement.EnumerateArray().Select(item => item.GetInt64()).ToArray();
        if (ids.Length is < 1 or > MaximumQuestions || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Exam question ids must be 1..50 distinct positive ids.", nameof(json));
        return ids;
    }

    public static void ValidatePaper(int durationMinutes, int passScore, int maxAttempts, int totalScore)
    {
        if (durationMinutes is < 1 or > 180) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        if (maxAttempts is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (totalScore <= 0 || passScore < 1 || passScore > totalScore)
            throw new ArgumentOutOfRangeException(nameof(passScore), "Pass score must be within the paper's total score.");
    }

    public static (bool Allowed, string Reason) CanStart(
        CrmExamPaperStatus status,
        int previousAttempts,
        int maxAttempts,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastStartedAtUtc)
    {
        if (status != CrmExamPaperStatus.Published) return (false, "paper_not_published");
        if (previousAttempts >= maxAttempts) return (false, "attempt_limit_reached");
        if (lastStartedAtUtc is not null && nowUtc - lastStartedAtUtc.Value < MinimumRetryGap)
            return (false, "retry_too_soon");
        return (true, "ok");
    }

    public static bool IsTimeout(DateTimeOffset startedAtUtc, DateTimeOffset nowUtc, int durationMinutes)
        => nowUtc - startedAtUtc > TimeSpan.FromMinutes(durationMinutes);

    /// <summary>Normalised correct-answer set. Multi-choice answers are comma-separated.</summary>
    public static IReadOnlyList<string> ParseCorrectAnswers(CrmExamQuestionType type, string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = type == CrmExamQuestionType.MultipleChoice
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [raw.Trim()];
        return parts.Where(value => value.Length > 0)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    public static void ValidateQuestion(
        CrmExamQuestionType type,
        IReadOnlyList<string> options,
        IReadOnlyList<string> correctAnswers)
    {
        if (options.Count < 2)
            throw new ArgumentException("An exam question needs at least two options.", nameof(options));
        if (correctAnswers.Count == 0)
            throw new ArgumentException("A correct answer is required.", nameof(correctAnswers));
        if (type == CrmExamQuestionType.MultipleChoice && correctAnswers.Count < 2)
            throw new ArgumentException("A multiple-choice question needs at least two correct answers.", nameof(correctAnswers));
        if (type != CrmExamQuestionType.MultipleChoice && correctAnswers.Count != 1)
            throw new ArgumentException("Single-choice questions accept exactly one correct answer.", nameof(correctAnswers));
        var available = options.Select(value => value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (correctAnswers.Any(answer => !available.Contains(answer)))
            throw new ArgumentException("Every correct answer must be one of the options.", nameof(correctAnswers));
    }

    public static bool IsAnswerCorrect(
        CrmExamQuestionType type,
        string correctAnswer,
        string submittedAnswer)
    {
        var expected = ParseCorrectAnswers(type, correctAnswer);
        var actual = ParseCorrectAnswers(type, submittedAnswer);
        if (type != CrmExamQuestionType.MultipleChoice && actual.Count > 1) actual = actual.Take(1).ToArray();
        return expected.Count > 0 && expected.SequenceEqual(actual);
    }

    public static CrmExamGradeResult Grade(
        IReadOnlyDictionary<long, string> answers,
        IReadOnlyList<SysCrmExamQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(questions);
        var verdicts = new List<CrmExamAnswerVerdict>(questions.Count);
        var score = 0;
        var total = 0;
        foreach (var question in questions)
        {
            total += Math.Max(0, question.Points);
            var correct = answers.TryGetValue(question.BasicId, out var answer)
                && IsAnswerCorrect(question.Type, question.CorrectAnswer, answer ?? string.Empty);
            if (correct) score += Math.Max(0, question.Points);
            verdicts.Add(new CrmExamAnswerVerdict(question.BasicId, correct));
        }
        return new CrmExamGradeResult(score, total, verdicts);
    }
}
