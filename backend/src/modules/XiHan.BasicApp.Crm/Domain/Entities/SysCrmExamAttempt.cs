using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>One exam attempt with bounded timing and an immutable submitted score.</summary>
[SugarTable(TableName = "Crm_Exam_Attempt", TableDescription = "CRM exam attempts")]
[SugarIndex("IX_{table}_Te_Pa_Us", nameof(TenantId), OrderByType.Asc, nameof(PaperId), OrderByType.Asc, nameof(UserId), OrderByType.Asc)]
public sealed class SysCrmExamAttempt : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Paper_Id", IsNullable = false)]
    public long PaperId { get; set; }

    [SugarColumn(ColumnName = "User_Id", IsNullable = false)]
    public long UserId { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmExamAttemptStatus Status { get; set; }

    [SugarColumn(ColumnName = "Started_At_Utc", IsNullable = false)]
    public DateTimeOffset StartedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Submitted_At_Utc", IsNullable = true)]
    public DateTimeOffset? SubmittedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Answers_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string AnswersJson { get; set; } = "{}";

    [SugarColumn(ColumnName = "Score", IsNullable = false, DefaultValue = "0")]
    public int Score { get; set; }

    [SugarColumn(ColumnName = "Total_Score", IsNullable = false, DefaultValue = "0")]
    public int TotalScore { get; set; }
}
