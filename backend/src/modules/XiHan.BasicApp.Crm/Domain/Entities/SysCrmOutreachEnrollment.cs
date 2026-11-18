using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>One candidate's enrollment in a sequence; eligibility and suppression are explicit.</summary>
[SugarTable(TableName = "Crm_Outreach_Enrollment", TableDescription = "KIGER CRM outreach enrollments")]
[SugarIndex("IX_{table}_Te_Se_St", nameof(TenantId), OrderByType.Asc, nameof(SequenceId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("UX_{table}_Te_Ca_Se_IsDe", nameof(TenantId), OrderByType.Asc, nameof(CandidateId), OrderByType.Asc, nameof(SequenceId), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysCrmOutreachEnrollment : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Sequence_Id", IsNullable = false)]
    public long SequenceId { get; set; }

    [SugarColumn(ColumnName = "Candidate_Id", IsNullable = false)]
    public long CandidateId { get; set; }

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmOutreachEnrollmentStatus Status { get; set; }

    [SugarColumn(ColumnName = "Current_Step", IsNullable = false, DefaultValue = "0")]
    public int CurrentStep { get; set; }

    [SugarColumn(ColumnName = "Next_Action_At_Utc", IsNullable = true)]
    public DateTimeOffset? NextActionAtUtc { get; set; }

    [SugarColumn(ColumnName = "Suppression_Reason", Length = 500, IsNullable = true)]
    public string? SuppressionReason { get; set; }
}
