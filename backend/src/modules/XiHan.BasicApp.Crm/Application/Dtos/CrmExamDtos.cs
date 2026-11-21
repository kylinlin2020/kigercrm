namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmExamSelectionRuleDto
{
    public string Category { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class CrmExamQuestionCreateDto
{
    public string Prompt { get; set; } = string.Empty;
    public int Type { get; set; }
    public string? Category { get; set; }
    public string[] Options { get; set; } = [];
    public string CorrectAnswer { get; set; } = string.Empty;
    /// <summary>Multiple-choice correct options; joined into CorrectAnswer on write.</summary>
    public string[] CorrectAnswers { get; set; } = [];
    public string? Explanation { get; set; }
    public int Points { get; set; } = 1;
}

/// <summary>Admin-facing question record (answer key visible only to managers).</summary>
public sealed class CrmExamQuestionDto
{
    public long BasicId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int Type { get; set; }
    public string? Category { get; set; }
    public string[] Options { get; set; } = [];
    public string CorrectAnswer { get; set; } = string.Empty;
    public string[] CorrectAnswers { get; set; } = [];
    public string? Explanation { get; set; }
    public int Points { get; set; }
}

/// <summary>Candidate-facing question: deliberately excludes CorrectAnswer and Explanation.</summary>
public sealed class CrmExamCandidateQuestionDto
{
    public long BasicId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int Type { get; set; }
    public string? Category { get; set; }
    public string[] Options { get; set; } = [];
    public int Points { get; set; }
}

public sealed class CrmExamPaperCreateDto
{
    public string Name { get; set; } = string.Empty;
    public long[] QuestionIds { get; set; } = [];
    /// <summary>When set, questions are selected by category at creation and QuestionIds is ignored.</summary>
    public CrmExamSelectionRuleDto[] SelectionRules { get; set; } = [];
    public int DurationMinutes { get; set; } = 30;
    public int PassScore { get; set; } = 60;
    public int MaxAttempts { get; set; } = 3;
}

public sealed class CrmExamPaperDto
{
    public long BasicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public CrmExamSelectionRuleDto[] SelectionRules { get; set; } = [];
    public int DurationMinutes { get; set; }
    public int PassScore { get; set; }
    public int MaxAttempts { get; set; }
    public int TotalScore { get; set; }
    public int Status { get; set; }
}

public sealed class CrmExamStartDto
{
    public long PaperId { get; set; }
}

public sealed class CrmExamSubmissionDto
{
    public long AttemptId { get; set; }
    public Dictionary<long, string> Answers { get; set; } = [];
}

public sealed class CrmExamAnswerVerdictDto
{
    public long QuestionId { get; set; }
    public bool Correct { get; set; }
}

public sealed class CrmExamAttemptDto
{
    public long BasicId { get; set; }
    public long PaperId { get; set; }
    public string PaperName { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public int Score { get; set; }
    public int TotalScore { get; set; }
    public IReadOnlyList<CrmExamCandidateQuestionDto> Questions { get; set; } = [];
    public IReadOnlyList<CrmExamAnswerVerdictDto> Verdicts { get; set; } = [];
}
