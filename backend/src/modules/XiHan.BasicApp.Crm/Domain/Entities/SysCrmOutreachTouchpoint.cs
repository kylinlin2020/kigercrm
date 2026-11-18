using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Immutable audit of one outreach action. Content is stored as a hash, not raw message text;
/// only a human-approved and operator-confirmed touchpoint can reach Sent state.
/// </summary>
[SugarTable(TableName = "Crm_Outreach_Touchpoint", TableDescription = "KIGER CRM outreach touchpoints")]
[SugarIndex("IX_{table}_Te_En_Step", nameof(TenantId), OrderByType.Asc, nameof(EnrollmentId), OrderByType.Asc, nameof(Step), OrderByType.Asc)]
[SugarIndex("IX_{table}_Te_Ca_St", nameof(TenantId), OrderByType.Asc, nameof(CandidateId), OrderByType.Asc, nameof(State), OrderByType.Asc)]
public sealed class SysCrmOutreachTouchpoint : BasicAppCreationEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Enrollment_Id", IsNullable = false)]
    public long EnrollmentId { get; set; }

    [SugarColumn(ColumnName = "Candidate_Id", IsNullable = false)]
    public long CandidateId { get; set; }

    [SugarColumn(ColumnName = "Step", IsNullable = false, DefaultValue = "0")]
    public int Step { get; set; }

    [SugarColumn(ColumnName = "Channel", IsNullable = false, DefaultValue = "0")]
    public CrmOutreachChannel Channel { get; set; }

    [SugarColumn(ColumnName = "State", IsNullable = false, DefaultValue = "0")]
    public CrmOutreachTouchpointState State { get; set; }

    [SugarColumn(ColumnName = "Reply_Class", IsNullable = false, DefaultValue = "0")]
    public CrmOutreachReplyClass ReplyClass { get; set; }

    [SugarColumn(ColumnName = "Content_Hash", Length = 64, IsNullable = false)]
    public string ContentHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Approved_By", IsNullable = true)]
    public long? ApprovedBy { get; set; }

    [SugarColumn(ColumnName = "Approved_At", IsNullable = true)]
    public DateTimeOffset? ApprovedAt { get; set; }

    [SugarColumn(ColumnName = "Occurred_At_Utc", IsNullable = true)]
    public DateTimeOffset? OccurredAtUtc { get; set; }
}
