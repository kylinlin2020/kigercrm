using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Immutable, frozen qualification report for one candidate; history is retained for audit.</summary>
[SugarTable(TableName = "Crm_Prospecting_Verification_Report", TableDescription = "KIGER CRM prospecting qualification reports")]
[SugarIndex("IX_{table}_Te_Ca_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CandidateId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
public sealed class SysCrmProspectingVerificationReport : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Candidate_Id", IsNullable = false)]
    public long CandidateId { get; set; }

    [SugarColumn(ColumnName = "Verdict", IsNullable = false, DefaultValue = "0")]
    public int Verdict { get; set; }

    [SugarColumn(ColumnName = "Identity_Authority", Length = 160, IsNullable = false)]
    public string IdentityAuthority { get; set; } = "none";

    [SugarColumn(ColumnName = "Snapshot_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string SnapshotJson { get; set; } = "{}";
}
