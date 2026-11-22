using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// A customer placed in the tenant's shared pool. Claiming is an exclusive, audited operation;
/// the claim expires after a bounded idle period and is recovered back to Available.
/// </summary>
[SugarTable(TableName = "Crm_Customer_Pool_Entry", TableDescription = "CRM customer pool entries")]
[SugarIndex("IX_{table}_Te_St_Claim", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(ClaimedAtUtc), OrderByType.Asc)]
[SugarIndex("UX_{table}_Te_Cu_IsDe", nameof(TenantId), OrderByType.Asc, nameof(CustomerId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysCrmCustomerPoolEntry : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Customer_Id", IsNullable = false)]
    public long CustomerId { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmCustomerPoolStatus Status { get; set; }

    [SugarColumn(ColumnName = "Claimed_User_Id", IsNullable = true)]
    public long? ClaimedUserId { get; set; }

    [SugarColumn(ColumnName = "Claimed_At_Utc", IsNullable = true)]
    public DateTimeOffset? ClaimedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Claim_Count", IsNullable = false, DefaultValue = "0")]
    public int ClaimCount { get; set; }

    [SugarColumn(ColumnName = "Last_Released_At_Utc", IsNullable = true)]
    public DateTimeOffset? LastReleasedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Note", Length = 500, IsNullable = true)]
    public string? Note { get; set; }
}
