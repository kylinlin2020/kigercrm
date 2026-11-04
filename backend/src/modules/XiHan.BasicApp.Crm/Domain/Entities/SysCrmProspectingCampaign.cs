using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Tenant-owned container for a prospecting objective and its approved strategy versions.</summary>
[SugarTable(TableName = "Crm_Prospecting_Campaign", TableDescription = "KIGER CRM prospecting campaigns")]
[SugarIndex("IX_{table}_TeId_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_CrId", nameof(CreatedId), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_IsDe", nameof(TenantId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_St_Cr", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_TeId_Ow", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc)]
public sealed class SysCrmProspectingCampaign : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Name", Length = 160, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Objective", Length = 1000, IsNullable = true)]
    public string? Objective { get; set; }

    [SugarColumn(ColumnName = "Product_Scope", Length = 500, IsNullable = true)]
    public string? ProductScope { get; set; }

    [SugarColumn(ColumnName = "Target_Markets", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? TargetMarkets { get; set; }

    [SugarColumn(ColumnName = "Ideal_Customer_Profile", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? IdealCustomerProfile { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public int Status { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", IsNullable = true)]
    public long? OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "Active_Strategy_Version", IsNullable = true)]
    public int? ActiveStrategyVersion { get; set; }
}
