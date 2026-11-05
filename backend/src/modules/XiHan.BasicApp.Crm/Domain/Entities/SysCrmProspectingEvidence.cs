using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Immutable, field-level provenance for a prospecting candidate.
/// Sensitive field values are intentionally not copied here; only a SHA-256 fingerprint is stored.
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_Evidence", TableDescription = "KIGER CRM prospecting field-level evidence")]
[SugarIndex("IX_{table}_Te_Ca_Fi", nameof(TenantId), OrderByType.Asc, nameof(CandidateId), OrderByType.Asc, nameof(FieldName), OrderByType.Asc)]
[SugarIndex("IX_{table}_Te_Pr_Ri", nameof(TenantId), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, nameof(RetrievedAtUtc), OrderByType.Desc)]
[SugarIndex("UX_{table}_EvidenceIdentity", nameof(TenantId), OrderByType.Asc, nameof(CandidateId), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, nameof(ProviderRecordId), OrderByType.Asc, nameof(FieldName), OrderByType.Asc, nameof(SourceUrlHash), OrderByType.Asc, nameof(RawPayloadHash), OrderByType.Asc, true)]
public sealed class SysCrmProspectingEvidence : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Candidate_Id", IsNullable = false)]
    public long CandidateId { get; set; }

    [SugarColumn(ColumnName = "Provider_Code", Length = 80, IsNullable = false)]
    public string ProviderCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Provider_Record_Id", Length = 240, IsNullable = false, DefaultValue = "")]
    public string ProviderRecordId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Field_Name", Length = 100, IsNullable = false)]
    public string FieldName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Field_Value_Hash", Length = 64, IsNullable = true)]
    public string? FieldValueHash { get; set; }

    [SugarColumn(ColumnName = "Source_Url", Length = 2000, IsNullable = false)]
    public string SourceUrl { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Source_Url_Hash", Length = 64, IsNullable = false, DefaultValue = "")]
    public string SourceUrlHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Evidence_Type", Length = 40, IsNullable = false)]
    public string EvidenceType { get; set; } = "direct";

    [SugarColumn(ColumnName = "Verification_Status", Length = 40, IsNullable = false)]
    public string VerificationStatus { get; set; } = "unverified";

    [SugarColumn(ColumnName = "Evidence_Summary", Length = 1000, IsNullable = true)]
    public string? EvidenceSummary { get; set; }

    [SugarColumn(ColumnName = "Published_At_Utc", IsNullable = true)]
    public DateTimeOffset? PublishedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Retrieved_At_Utc", IsNullable = false)]
    public DateTimeOffset RetrievedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Raw_Payload_Hash", Length = 64, IsNullable = false, DefaultValue = "")]
    public string RawPayloadHash { get; set; } = string.Empty;
}
