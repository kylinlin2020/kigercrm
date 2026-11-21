using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Published exam definition: frozen question ids, duration and pass threshold.</summary>
[SugarTable(TableName = "Crm_Exam_Paper", TableDescription = "CRM exam papers")]
[SugarIndex("IX_{table}_Te_St_CrTi", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysCrmExamPaper : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Name", Length = 160, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Question_Ids_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string QuestionIdsJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "Duration_Minutes", IsNullable = false, DefaultValue = "30")]
    public int DurationMinutes { get; set; } = 30;

    [SugarColumn(ColumnName = "Pass_Score", IsNullable = false, DefaultValue = "60")]
    public int PassScore { get; set; } = 60;

    [SugarColumn(ColumnName = "Max_Attempts", IsNullable = false, DefaultValue = "3")]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Frozen selection rules (category -> count) used to compose QuestionIdsJson at creation.</summary>
    [SugarColumn(ColumnName = "Selection_Rules_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? SelectionRulesJson { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmExamPaperStatus Status { get; set; }
}
