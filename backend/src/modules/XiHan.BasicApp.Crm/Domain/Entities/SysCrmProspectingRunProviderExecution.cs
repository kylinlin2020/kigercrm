using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Durable per-provider step within an immutable prospecting Run plan.
/// This row is an execution checkpoint, not an HTTP request-attempt log.
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_Run_Provider_Execution", TableDescription = "KIGER CRM durable provider execution checkpoints")]
[SugarIndex("UX_{table}_Te_Ru_Pl_Pr", nameof(TenantId), OrderByType.Asc, nameof(RunId), OrderByType.Asc, nameof(PlanVersion), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_Te_Ru_St", nameof(TenantId), OrderByType.Asc, nameof(RunId), OrderByType.Asc, nameof(State), OrderByType.Asc)]
public sealed class SysCrmProspectingRunProviderExecution : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Run_Id", IsNullable = false)]
    public long RunId { get; set; }

    [SugarColumn(ColumnName = "Plan_Version", IsNullable = false)]
    public int PlanVersion { get; set; }

    [SugarColumn(ColumnName = "Plan_Ordinal", IsNullable = false)]
    public int PlanOrdinal { get; set; }

    [SugarColumn(ColumnName = "Provider_Code", Length = 80, IsNullable = false)]
    public string ProviderCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Request_Fingerprint", Length = 64, IsNullable = false)]
    public string RequestFingerprint { get; set; } = string.Empty;

    // Queued, Started, Checkpointed, Projecting, Succeeded, Failed, NeedsReview, Skipped.
    [SugarColumn(ColumnName = "State", Length = 24, IsNullable = false, DefaultValue = "Queued")]
    public string State { get; set; } = "Queued";

    [SugarColumn(ColumnName = "Attempt_Count", IsNullable = false, DefaultValue = "0")]
    public int AttemptCount { get; set; }

    [SugarColumn(ColumnName = "Checkpoint_Json", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = true)]
    public string? CheckpointJson { get; set; }

    [SugarColumn(ColumnName = "Checkpoint_Hash", Length = 64, IsNullable = true)]
    public string? CheckpointHash { get; set; }

    [SugarColumn(ColumnName = "Checkpoint_Bytes", IsNullable = false, DefaultValue = "0")]
    public int CheckpointBytes { get; set; }

    [SugarColumn(ColumnName = "Raw_Record_Count", IsNullable = false, DefaultValue = "0")]
    public int RawRecordCount { get; set; }

    [SugarColumn(ColumnName = "Projected_Record_Count", IsNullable = false, DefaultValue = "0")]
    public int ProjectedRecordCount { get; set; }

    [SugarColumn(ColumnName = "Failure_Count", IsNullable = false, DefaultValue = "0")]
    public int FailureCount { get; set; }

    [SugarColumn(ColumnName = "Started_At", IsNullable = true)]
    public DateTimeOffset? StartedAt { get; set; }

    [SugarColumn(ColumnName = "Completed_At", IsNullable = true)]
    public DateTimeOffset? CompletedAt { get; set; }

    [SugarColumn(ColumnName = "Failure_Code", Length = 80, IsNullable = true)]
    public string? FailureCode { get; set; }

    [SugarColumn(ColumnName = "Failure_Summary", Length = 1000, IsNullable = true)]
    public string? FailureSummary { get; set; }
}
