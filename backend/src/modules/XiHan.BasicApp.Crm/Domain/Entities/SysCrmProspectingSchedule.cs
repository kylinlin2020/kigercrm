using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Approved recurring prospecting plan. The scheduler only reads Approved rows with a due
/// NextRunAtUtc, advances the occurrence with an optimistic compare-and-swap, and lets the existing
/// StartRun pipeline create the Run (the request key carries the occurrence, so replay is idempotent).
/// </summary>
[SugarTable(TableName = "Crm_Prospecting_Schedule", TableDescription = "KIGER CRM recurring prospecting schedules")]
[SugarIndex("IX_{table}_Te_Ca_CrTi", nameof(TenantId), OrderByType.Asc, nameof(CampaignId), OrderByType.Asc, nameof(CreatedTime), OrderByType.Desc)]
[SugarIndex("IX_{table}_Te_St_Next", nameof(TenantId), OrderByType.Asc, nameof(Status), OrderByType.Asc, nameof(NextRunAtUtc), OrderByType.Asc)]
[SugarIndex("UX_{table}_Te_Rq_IsDe", nameof(TenantId), OrderByType.Asc, nameof(RequestKeyPrefix), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysCrmProspectingSchedule : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Campaign_Id", IsNullable = false)]
    public long CampaignId { get; set; }

    [SugarColumn(ColumnName = "Name", Length = 160, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Stable prefix; the full Run request key is prefix + occurrence timestamp.</summary>
    [SugarColumn(ColumnName = "Request_Key_Prefix", Length = 100, IsNullable = false)]
    public string RequestKeyPrefix { get; set; } = string.Empty;

    /// <summary>IANA time zone id (e.g. Asia/Shanghai); all recurrence math happens in local wall time.</summary>
    [SugarColumn(ColumnName = "Time_Zone_Id", Length = 100, IsNullable = false)]
    public string TimeZoneId { get; set; } = "UTC";

    [SugarColumn(ColumnName = "Frequency", IsNullable = false, DefaultValue = "0")]
    public CrmProspectingScheduleFrequency Frequency { get; set; }

    [SugarColumn(ColumnName = "Interval", IsNullable = false, DefaultValue = "1")]
    public int Interval { get; set; } = 1;

    /// <summary>Minutes from local midnight (0–1439).</summary>
    [SugarColumn(ColumnName = "Time_Of_Day_Minutes", IsNullable = false, DefaultValue = "0")]
    public int TimeOfDayMinutes { get; set; }

    /// <summary>Weekly day (0 = Sunday … 6 = Saturday).</summary>
    [SugarColumn(ColumnName = "Day_Of_Week", IsNullable = true)]
    public int? DayOfWeek { get; set; }

    /// <summary>Monthly day (1–31) or -1 for the last day of the month.</summary>
    [SugarColumn(ColumnName = "Day_Of_Month", IsNullable = true)]
    public int? DayOfMonth { get; set; }

    [SugarColumn(ColumnName = "Start_Date_Utc", IsNullable = false)]
    public DateTimeOffset StartDateUtc { get; set; }

    [SugarColumn(ColumnName = "End_Date_Utc", IsNullable = true)]
    public DateTimeOffset? EndDateUtc { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmProspectingScheduleStatus Status { get; set; }

    [SugarColumn(ColumnName = "Next_Run_At_Utc", IsNullable = true)]
    public DateTimeOffset? NextRunAtUtc { get; set; }

    [SugarColumn(ColumnName = "Last_Run_At_Utc", IsNullable = true)]
    public DateTimeOffset? LastRunAtUtc { get; set; }

    [SugarColumn(ColumnName = "Last_Run_Id", IsNullable = true)]
    public long? LastRunId { get; set; }

    [SugarColumn(ColumnName = "Approved_By", IsNullable = true)]
    public long? ApprovedBy { get; set; }

    [SugarColumn(ColumnName = "Approved_At", IsNullable = true)]
    public DateTimeOffset? ApprovedAt { get; set; }
}
