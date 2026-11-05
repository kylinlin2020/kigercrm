using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Durable tenant-scoped state for one provider-side asynchronous extraction.
/// Provider IDs and cursors are opaque untrusted values; never interpret them as URLs or paths.
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_Extraction_Job", TableDescription = "KIGER CRM durable provider extraction jobs")]
[SugarIndex("UX_{table}_Te_Ru_Pr_Fp", nameof(TenantId), OrderByType.Asc, nameof(RunId), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, nameof(RequestFingerprint), OrderByType.Asc, true)]
[SugarIndex("IX_{table}_Te_St_Np", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(NextPollAtUtc), OrderByType.Asc)]
[SugarIndex("IX_{table}_Te_Pr_Ex", nameof(TenantId), OrderByType.Asc, nameof(ProviderCode), OrderByType.Asc, nameof(ProviderExtractionId), OrderByType.Asc)]
public sealed class SysCrmProspectingExtractionJob : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Run_Id", IsNullable = false)]
    public long RunId { get; set; }

    [SugarColumn(ColumnName = "Provider_Code", Length = 80, IsNullable = false)]
    public string ProviderCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Request_Fingerprint", Length = 64, IsNullable = false)]
    public string RequestFingerprint { get; set; } = string.Empty;

    /// <summary>Stable local idempotency key passed to providers that support idempotent submission.</summary>
    [SugarColumn(ColumnName = "Submission_Idempotency_Key", IsNullable = false)]
    public Guid SubmissionIdempotencyKey { get; set; }

    [SugarColumn(ColumnName = "Provider_Extraction_Id", Length = 512, IsNullable = true)]
    public string? ProviderExtractionId { get; set; }

    [SugarColumn(ColumnName = "Status", Length = 24, IsNullable = false)]
    public string Status { get; set; } = "Pending";

    [SugarColumn(ColumnName = "Submitted_At_Utc", IsNullable = true)]
    public DateTimeOffset? SubmittedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Last_Polled_At_Utc", IsNullable = true)]
    public DateTimeOffset? LastPolledAtUtc { get; set; }

    [SugarColumn(ColumnName = "Next_Poll_At_Utc", IsNullable = false)]
    public DateTimeOffset NextPollAtUtc { get; set; }

    [SugarColumn(ColumnName = "Expires_At_Utc", IsNullable = true)]
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    [SugarColumn(ColumnName = "Poll_Attempt_Count", IsNullable = false, DefaultValue = "0")]
    public int PollAttemptCount { get; set; }

    [SugarColumn(ColumnName = "Result_Cursor", Length = 2048, IsNullable = true)]
    public string? ResultCursor { get; set; }

    [SugarColumn(ColumnName = "Safe_Metadata_Json", Length = 4000, IsNullable = true)]
    public string? SafeMetadataJson { get; set; }

    [SugarColumn(ColumnName = "Failure_Code", Length = 80, IsNullable = true)]
    public string? FailureCode { get; set; }

    [SugarColumn(ColumnName = "Failure_Summary", Length = 1000, IsNullable = true)]
    public string? FailureSummary { get; set; }

    [SugarColumn(ColumnName = "Result_Record_Count", IsNullable = false, DefaultValue = "0")]
    public int ResultRecordCount { get; set; }

    /// <summary>Accumulated provider record failures across every fetched page; any positive value blocks successful completion.</summary>
    [SugarColumn(ColumnName = "Result_Failure_Count", IsNullable = false, DefaultValue = "0")]
    public int ResultFailureCount { get; set; }

    [SugarColumn(ColumnName = "Lease_Owner", Length = 160, IsNullable = true)]
    public string? LeaseOwner { get; set; }

    [SugarColumn(ColumnName = "Lease_Token", IsNullable = true)]
    public Guid? LeaseToken { get; set; }

    [SugarColumn(ColumnName = "Lease_Expires_At_Utc", IsNullable = true)]
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    [SugarColumn(ColumnName = "Fencing_Version", IsNullable = false, DefaultValue = "0")]
    public long FencingVersion { get; set; }
}
