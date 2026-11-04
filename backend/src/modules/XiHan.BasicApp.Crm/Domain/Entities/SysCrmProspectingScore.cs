using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>Immutable, versioned result of deterministic prospecting prioritization.</summary>
[SugarTable(TableName = "Crm_Prospecting_Score", TableDescription = "KIGER CRM prospecting score history")]
[SugarIndex("IX_{table}_Te_Ca_Ev", nameof(TenantId), OrderByType.Asc, nameof(CandidateId), OrderByType.Asc, nameof(EvaluatedAtUtc), OrderByType.Desc)]
[SugarIndex("IX_{table}_Te_Sv_Pr", nameof(TenantId), OrderByType.Asc, nameof(ScorecardVersion), OrderByType.Asc, nameof(Priority), OrderByType.Desc)]
[SugarIndex("UX_{table}_Te_IdemKey", nameof(TenantId), OrderByType.Asc, nameof(IdempotencyKey), OrderByType.Asc, true)]
public sealed class SysCrmProspectingScore : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Candidate_Id", IsNullable = false)]
    public long CandidateId { get; set; }

    /// <summary>Optional stable key for one-time worker snapshots; null preserves ordinary score history.</summary>
    [SugarColumn(ColumnName = "Idempotency_Key", Length = 160, IsNullable = true)]
    public string? IdempotencyKey { get; set; }

    [SugarColumn(ColumnName = "Scorecard_Version", Length = 80, IsNullable = false)]
    public string ScorecardVersion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Total", IsNullable = false)]
    public int Total { get; set; }

    [SugarColumn(ColumnName = "Priority", IsNullable = false)]
    public int Priority { get; set; }

    [SugarColumn(ColumnName = "Identity_Points", IsNullable = false)]
    public int IdentityPoints { get; set; }

    [SugarColumn(ColumnName = "Procurement_Points", IsNullable = false)]
    public int ProcurementPoints { get; set; }

    [SugarColumn(ColumnName = "Product_Fit_Points", IsNullable = false)]
    public int ProductFitPoints { get; set; }

    [SugarColumn(ColumnName = "Market_Fit_Points", IsNullable = false)]
    public int MarketFitPoints { get; set; }

    [SugarColumn(ColumnName = "Recency_Points", IsNullable = false)]
    public int RecencyPoints { get; set; }

    [SugarColumn(ColumnName = "Evidence_Points", IsNullable = false)]
    public int EvidencePoints { get; set; }

    [SugarColumn(ColumnName = "Contact_Points", IsNullable = false)]
    public int ContactPoints { get; set; }

    [SugarColumn(ColumnName = "Review_Gates_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string ReviewGatesJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "Evaluated_At_Utc", IsNullable = false)]
    public DateTimeOffset EvaluatedAtUtc { get; set; }

    /// <summary>Bounded, immutable references to the exact evidence rows used by this score.</summary>
    [SugarColumn(ColumnName = "Evidence_References_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false, DefaultValue = "[]")]
    public string EvidenceReferencesJson { get; set; } = "[]";
}
