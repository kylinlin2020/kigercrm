using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Assignable task with an optional CRM record association.</summary>
[SugarTable(TableName = "Crm_Task", TableDescription = "KIGER CRM tasks and reminders")]
[SugarIndex("IX_{table}_TeId_As_DuAt", nameof(TenantId), OrderByType.Asc, nameof(AssigneeUserId), OrderByType.Asc, nameof(DueAt), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_St_DuAt", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(DueAt), OrderByType.Asc)]
public sealed class SysCrmTask : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Related_Entity_Type", Length = 40, IsNullable = true)]
    public string? RelatedEntityType { get; set; }

    [SugarColumn(ColumnName = "Related_Entity_Id", IsNullable = true)]
    public long? RelatedEntityId { get; set; }

    [SugarColumn(ColumnName = "Title", Length = 200, IsNullable = false)]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Description", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Description { get; set; }

    [SugarColumn(ColumnName = "Due_At", IsNullable = true)]
    public DateTime? DueAt { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false)]
    public CrmTaskStatus Status { get; set; } = CrmTaskStatus.Open;

    [SugarColumn(ColumnName = "Priority", IsNullable = false)]
    public int Priority { get; set; } = 2;

    [SugarColumn(ColumnName = "Assignee_User_Id", IsNullable = true)]
    public long? AssigneeUserId { get; set; }

    [SugarColumn(ColumnName = "Completed_At", IsNullable = true)]
    public DateTime? CompletedAt { get; set; }
}
