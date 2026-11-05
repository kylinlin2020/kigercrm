using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Normalized prospecting result awaiting human review; never a CRM Lead by itself.</summary>
[SugarTable(TableName = "Crm_Prospecting_Candidate", TableDescription = "KIGER CRM prospecting review candidates")]
[SugarIndex("UX_{table}_Te_Ru_Pr_Ex_Is", nameof(TenantId), OrderByType.Asc, nameof(RunId), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, nameof(ExternalId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_Te_Ru_St_Cr", nameof(TenantId), OrderByType.Asc, nameof(RunId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_Te_Em", nameof(TenantId), OrderByType.Asc, nameof(NormalizedEmail), OrderByType.Asc)]
[SugarIndex("IX_{table}_Te_Web", nameof(TenantId), OrderByType.Asc, nameof(WebsiteHost), OrderByType.Asc)]
public sealed class SysCrmProspectingCandidate : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Run_Id", IsNullable = false)]
    public long RunId { get; set; }
    [SugarColumn(ColumnName = "Provider_Code", Length = 80, IsNullable = false)]
    public string ProviderCode { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "External_Id", Length = 200, IsNullable = false)]
    public string ExternalId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "Source", Length = 200, IsNullable = true)]
    public string? Source { get; set; }
    [SugarColumn(ColumnName = "Title", Length = 300, IsNullable = true)]
    public string? Title { get; set; }
    [SugarColumn(ColumnName = "Company_Name", Length = 200, IsNullable = false)]
    public string CompanyName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "Contact_Name", Length = 200, IsNullable = true)]
    public string? ContactName { get; set; }
    [SugarColumn(ColumnName = "Business_Email", Length = 254, IsNullable = true)]
    public string? BusinessEmail { get; set; }
    [SugarColumn(ColumnName = "Normalized_Email", Length = 254, IsNullable = true)]
    public string? NormalizedEmail { get; set; }
    [SugarColumn(ColumnName = "Website", Length = 500, IsNullable = true)]
    public string? Website { get; set; }
    [SugarColumn(ColumnName = "Website_Host", Length = 500, IsNullable = true)]
    public string? WebsiteHost { get; set; }
    [SugarColumn(ColumnName = "Summary", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? Summary { get; set; }
    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmProspectingCandidateStatus Status { get; set; }
    [SugarColumn(ColumnName = "Duplicate_Lead_Id", IsNullable = true)]
    public long? DuplicateLeadId { get; set; }
    [SugarColumn(ColumnName = "Duplicate_Reason", Length = 500, IsNullable = true)]
    public string? DuplicateReason { get; set; }
    [SugarColumn(ColumnName = "Converted_Lead_Id", IsNullable = true)]
    public long? ConvertedLeadId { get; set; }
}
