using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Append-only operational record for one provider invocation attempt.
/// It stores only a canonical request fingerprint and bounded diagnostics, never credentials or raw HTTP headers.
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_Provider_Request", TableDescription = "KIGER CRM prospecting provider request attempts")]
[SugarIndex("UX_{table}_Te_Ru_Pr_At", nameof(TenantId), OrderByType.Asc, nameof(RunId), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, nameof(AttemptNo), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_Te_Pr_St", nameof(TenantId), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, nameof(StartedAt), OrderByType.Desc)]
[SugarIndex("IX_{table}_Te_St_St", nameof(TenantId), OrderByType.Asc, nameof(State), OrderByType.Asc, nameof(StartedAt), OrderByType.Desc)]
[SugarIndex("IX_{table}_Te_Rq_St", nameof(TenantId), OrderByType.Asc, nameof(RequestKey), OrderByType.Asc, nameof(StartedAt), OrderByType.Desc)]
public sealed class SysCrmProspectingProviderRequest : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Run_Id", IsNullable = false)]
    public long RunId { get; set; }

    [SugarColumn(ColumnName = "Provider_Code", Length = 80, IsNullable = false)]
    public string ProviderCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Attempt_No", IsNullable = false)]
    public int AttemptNo { get; set; }

    [SugarColumn(ColumnName = "Request_Key", Length = 64, IsNullable = false)]
    public string RequestKey { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "State", Length = 24, IsNullable = false)]
    public string State { get; set; } = "Started";

    [SugarColumn(ColumnName = "Started_At", IsNullable = false)]
    public DateTimeOffset StartedAt { get; set; }

    [SugarColumn(ColumnName = "Completed_At", IsNullable = true)]
    public DateTimeOffset? CompletedAt { get; set; }

    [SugarColumn(ColumnName = "Duration_Ms", IsNullable = true)]
    public long? DurationMs { get; set; }

    [SugarColumn(ColumnName = "Http_Status", IsNullable = true)]
    public int? HttpStatus { get; set; }

    [SugarColumn(ColumnName = "Result_Count", IsNullable = false, DefaultValue = "0")]
    public int ResultCount { get; set; }

    [SugarColumn(ColumnName = "Failure_Code", Length = 80, IsNullable = true)]
    public string? FailureCode { get; set; }

    [SugarColumn(ColumnName = "Failure_Summary", Length = 1000, IsNullable = true)]
    public string? FailureSummary { get; set; }

    [SugarColumn(ColumnName = "Retry_After_At", IsNullable = true)]
    public DateTimeOffset? RetryAfterAt { get; set; }

    [SugarColumn(ColumnName = "Response_Hash", Length = 64, IsNullable = true)]
    public string? ResponseHash { get; set; }

    [SugarColumn(ColumnName = "Cache_Hit", IsNullable = false, DefaultValue = "false")]
    public bool CacheHit { get; set; }
}
