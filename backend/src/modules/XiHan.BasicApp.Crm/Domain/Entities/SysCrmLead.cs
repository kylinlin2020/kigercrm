using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Prospective customer requiring qualification and follow-up.</summary>
[SugarTable(TableName = "Crm_Lead", TableDescription = "KIGER CRM leads")]
[SugarIndex("IX_{table}_TeId_St_CrTi", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_TeId_OwId", nameof(TenantId), OrderByType.Asc, nameof(OwnerUserId), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_Em", nameof(TenantId), OrderByType.Asc, nameof(Email), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_DupEm", nameof(TenantId), OrderByType.Asc, nameof(DuplicateEmailNormalized), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_PhoneD", nameof(TenantId), OrderByType.Asc, nameof(PhoneDigits), OrderByType.Asc)]
[SugarIndex("IX_{table}_TeId_WebHost", nameof(TenantId), OrderByType.Asc, nameof(WebsiteHost), OrderByType.Asc)]
public sealed class SysCrmLead : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Company_Name", Length = 200, IsNullable = false)]
    public string CompanyName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Contact_Name", Length = 200, IsNullable = true)]
    public string? ContactName { get; set; }

    [SugarColumn(ColumnName = "Email", Length = 254, IsNullable = true)]
    public string? Email { get; set; }

    [SugarColumn(ColumnName = "Phone", Length = 40, IsNullable = true)]
    public string? Phone { get; set; }

    [SugarColumn(ColumnName = "Website", Length = 500, IsNullable = true)]
    public string? Website { get; set; }

    [SugarColumn(ColumnName = "Duplicate_Email_Normalized", Length = 254, IsNullable = true)]
    public string? DuplicateEmailNormalized { get; set; }

    [SugarColumn(ColumnName = "Phone_Digits", Length = 40, IsNullable = true)]
    public string? PhoneDigits { get; set; }

    [SugarColumn(ColumnName = "Website_Host", Length = 500, IsNullable = true)]
    public string? WebsiteHost { get; set; }

    [SugarColumn(ColumnName = "Country_Region", Length = 100, IsNullable = true)]
    public string? CountryRegion { get; set; }

    [SugarColumn(ColumnName = "Source", Length = 80, IsNullable = true)]
    public string? Source { get; set; }

    [SugarColumn(ColumnName = "Source_Reference", Length = 500, IsNullable = true)]
    public string? SourceReference { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false)]
    public CrmLeadStatus Status { get; set; } = CrmLeadStatus.New;

    [SugarColumn(ColumnName = "Score", IsNullable = false)]
    public int Score { get; set; }

    [SugarColumn(ColumnName = "Owner_User_Id", IsNullable = true)]
    public long? OwnerUserId { get; set; }

    [SugarColumn(ColumnName = "Converted_Customer_Id", IsNullable = true)]
    public long? ConvertedCustomerId { get; set; }

    [SugarColumn(ColumnName = "Converted_Contact_Id", IsNullable = true)]
    public long? ConvertedContactId { get; set; }

    [SugarColumn(ColumnName = "Last_Contacted_At", IsNullable = true)]
    public DateTime? LastContactedAt { get; set; }

    [SugarColumn(ColumnName = "Notes", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Notes { get; set; }
}
