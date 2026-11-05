using SqlSugar;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// A correctness checkpoint for one fully completed provider scan within a campaign scope.
/// It stores no provider payload; the scope hash is the canonical request fingerprint.
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_Provider_Coverage", TableDescription = "KIGER CRM prospecting provider coverage checkpoints")]
[SugarIndex("IX_{table}_Te_Ca_ScAt", nameof(TenantId), OrderByType.Asc, nameof(CampaignId), OrderByType.Asc, nameof(LastSuccessfulScanAt), OrderByType.Desc)]
public sealed class SysCrmProspectingProviderCoverage : IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Tenant_Id", IsPrimaryKey = true, IsNullable = false)]
    public long TenantId { get; set; }

    [SugarColumn(ColumnName = "Campaign_Id", IsPrimaryKey = true, IsNullable = false)]
    public long CampaignId { get; set; }

    [SugarColumn(ColumnName = "Provider_Code", Length = 80, IsPrimaryKey = true, IsNullable = false)]
    public string ProviderCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Coverage_Scope_Hash", Length = 64, IsPrimaryKey = true, IsNullable = false)]
    public string CoverageScopeHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Last_Successful_Scan_At", IsNullable = false)]
    public DateTimeOffset LastSuccessfulScanAt { get; set; }

    [SugarColumn(ColumnName = "Last_Run_Id", IsNullable = false)]
    public long LastRunId { get; set; }

    [SugarColumn(ColumnName = "Last_Result_Count", IsNullable = false, DefaultValue = "0")]
    public int LastResultCount { get; set; }

    [SugarColumn(ColumnName = "Last_Response_Hash", Length = 64, IsNullable = true)]
    public string? LastResponseHash { get; set; }

    [SugarColumn(ColumnName = "Version", IsNullable = false, DefaultValue = "1")]
    public long Version { get; set; }
}
