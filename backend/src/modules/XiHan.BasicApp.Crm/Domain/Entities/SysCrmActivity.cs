using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Recorded interaction associated with a CRM record.</summary>
[SugarTable(TableName = "Crm_Activity", TableDescription = "KIGER CRM activities and follow-ups")]
[SugarIndex("IX_{table}_TeId_Rel_CrAt", nameof(TenantId), OrderByType.Asc, nameof(RelatedEntityType), OrderByType.Asc, nameof(RelatedEntityId), OrderByType.Asc, nameof(OccurredAt), OrderByType.Desc)]
[SugarIndex("IX_{table}_TeId_OwId", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc)]
public sealed class SysCrmActivity : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Related_Entity_Type", Length = 40, IsNullable = false)]
    public string RelatedEntityType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Related_Entity_Id", IsNullable = false)]
    public long RelatedEntityId { get; set; }

    [SugarColumn(ColumnName = "Activity_Type", IsNullable = false)]
    public CrmActivityType ActivityType { get; set; } = CrmActivityType.Note;

    [SugarColumn(ColumnName = "Subject", Length = 200, IsNullable = false)]
    public string Subject { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Content", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Content { get; set; }

    [SugarColumn(ColumnName = "Occurred_At", IsNullable = false)]
    public DateTime OccurredAt { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", IsNullable = true)]
    public long? OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "External_System", Length = 80, IsNullable = true)]
    public string? ExternalSystem { get; set; }

    [SugarColumn(ColumnName = "External_Id", Length = 160, IsNullable = true)]
    public string? ExternalId { get; set; }
}
