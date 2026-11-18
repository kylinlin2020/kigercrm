using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Bounded outreach sequence. Steps are frozen JSON instructions for human execution; this module
/// never sends external messages. Only an approved touchpoint can be recorded as sent by an operator.
/// </summary>
[SugarTable(TableName = "Crm_Outreach_Sequence", TableDescription = "KIGER CRM outreach sequences")]
[SugarIndex("IX_{table}_Te_St_CrTi", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("UX_{table}_Te_Name_IsDe", nameof(TenantId), OrderByType.Asc, nameof(Name), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysCrmOutreachSequence : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Name", Length = 160, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Steps_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string StepsJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmOutreachSequenceStatus Status { get; set; }

    [SugarColumn(ColumnName = "Activated_At", IsNullable = true)]
    public DateTimeOffset? ActivatedAt { get; set; }
}
