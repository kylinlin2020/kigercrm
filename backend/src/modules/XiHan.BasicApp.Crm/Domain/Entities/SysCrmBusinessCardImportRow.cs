using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// One operator-reviewed row extracted from an OCR payload. The raw OCR text stays server-side;
/// only explicit confirmation creates a Lead.
/// </summary>
[SugarTable(TableName = "Crm_Business_Card_Import_Row", TableDescription = "CRM business card OCR import rows")]
[SugarIndex("IX_{table}_Te_Ba_St", nameof(TenantId), OrderByType.Asc, nameof(BatchId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class SysCrmBusinessCardImportRow : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Batch_Id", Length = 80, IsNullable = false)]
    public string BatchId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "External_Id", Length = 80, IsNullable = false)]
    public string ExternalId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Contact_Name", Length = 200, IsNullable = true)]
    public string? ContactName { get; set; }

    [SugarColumn(ColumnName = "Company_Name", Length = 200, IsNullable = true)]
    public string? CompanyName { get; set; }

    [SugarColumn(ColumnName = "Phone", Length = 40, IsNullable = true)]
    public string? Phone { get; set; }

    [SugarColumn(ColumnName = "Email", Length = 254, IsNullable = true)]
    public string? Email { get; set; }

    [SugarColumn(ColumnName = "Job_Title", Length = 120, IsNullable = true)]
    public string? JobTitle { get; set; }

    [SugarColumn(ColumnName = "Website", Length = 500, IsNullable = true)]
    public string? Website { get; set; }

    [SugarColumn(ColumnName = "Raw_Ocr_Text", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? RawOcrText { get; set; }

    [SugarColumn(ColumnName = "Normalized_Email", Length = 254, IsNullable = true)]
    public string? NormalizedEmail { get; set; }

    [SugarColumn(ColumnName = "Phone_Digits", Length = 40, IsNullable = true)]
    public string? PhoneDigits { get; set; }

    [SugarColumn(ColumnName = "Website_Host", Length = 500, IsNullable = true)]
    public string? WebsiteHost { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmBusinessCardImportStatus Status { get; set; }

    [SugarColumn(ColumnName = "Duplicate_Reason", Length = 500, IsNullable = true)]
    public string? DuplicateReason { get; set; }

    [SugarColumn(ColumnName = "Reject_Reason", Length = 1000, IsNullable = true)]
    public string? RejectReason { get; set; }

    [SugarColumn(ColumnName = "Converted_Lead_Id", IsNullable = true)]
    public long? ConvertedLeadId { get; set; }
}
