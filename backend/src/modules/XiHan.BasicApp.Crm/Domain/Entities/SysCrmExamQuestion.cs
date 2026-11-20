using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Tenant-scoped question bank entry; correct answer and explanation stay server-side.</summary>
[SugarTable(TableName = "Crm_Exam_Question", TableDescription = "CRM exam questions")]
[SugarIndex("IX_{table}_Te_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysCrmExamQuestion : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Prompt", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string Prompt { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Type", IsNullable = false, DefaultValue = "0")]
    public CrmExamQuestionType Type { get; set; }

    /// <summary>JSON array of answer options; never returned in candidate-facing attempts.</summary>
    [SugarColumn(ColumnName = "Options_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string OptionsJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "Correct_Answer", Length = 40, IsNullable = false)]
    public string CorrectAnswer { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Explanation", Length = 1000, IsNullable = true)]
    public string? Explanation { get; set; }

    [SugarColumn(ColumnName = "Points", IsNullable = false, DefaultValue = "1")]
    public int Points { get; set; } = 1;
}
