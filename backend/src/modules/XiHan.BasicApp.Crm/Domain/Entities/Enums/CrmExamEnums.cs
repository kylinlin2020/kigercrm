namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

public enum CrmExamQuestionType
{
    SingleChoice = 0,
    TrueFalse = 1,
    MultipleChoice = 2,
}

public enum CrmExamPaperStatus
{
    Draft = 0,
    Published = 1,
    Retired = 2,
}

public enum CrmExamAttemptStatus
{
    InProgress = 0,
    Submitted = 1,
    Passed = 2,
    Failed = 3,
}
