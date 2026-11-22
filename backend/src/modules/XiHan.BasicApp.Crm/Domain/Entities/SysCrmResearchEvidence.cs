using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// One structured fact contributing to a research report. FactKey is a stable token rendered by
/// the client's i18n layer; FactValue is the raw CRM value, never a fabricated or inferred claim.
/// </summary>
[SugarTable(TableName = "Crm_Research_Evidence", TableDescription = "CRM research evidence facts")]
[SugarIndex("IX_{table}_Te_Re_So", nameof(TenantId), OrderByType.Asc, nameof(ReportId), OrderByType.Asc, nameof(Source), OrderByType.Asc)]
public sealed class SysCrmResearchEvidence : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Report_Id", IsNullable = false)]
    public long ReportId { get; set; }

    [SugarColumn(ColumnName = "Source", IsNullable = false)]
    public CrmResearchSource Source { get; set; }

    [SugarColumn(ColumnName = "Fact_Key", Length = 80, IsNullable = false)]
    public string FactKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Fact_Value", Length = 500, IsNullable = false)]
    public string FactValue { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Source_Ref", Length = 200, IsNullable = true)]
    public string? SourceRef { get; set; }

    [SugarColumn(ColumnName = "Confidence", IsNullable = false, DefaultValue = "0")]
    public int Confidence { get; set; }
}
