using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// A rendered email draft awaiting explicit human approval. The rendered text is stored so a
/// reviewer can see exactly what will be sent; nothing is delivered by this module.
/// </summary>
[SugarTable(TableName = "Crm_Outreach_Draft", TableDescription = "CRM rendered outreach drafts")]
[SugarIndex("IX_{table}_Te_En_St", nameof(TenantId), OrderByType.Asc, nameof(EnrollmentId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("IX_{table}_Te_Ca_St", nameof(TenantId), OrderByType.Asc, nameof(CandidateId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
public sealed class SysCrmOutreachDraft : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Enrollment_Id", IsNullable = false)]
    public long EnrollmentId { get; set; }

    [SugarColumn(ColumnName = "Candidate_Id", IsNullable = false)]
    public long CandidateId { get; set; }

    [SugarColumn(ColumnName = "Step", IsNullable = false, DefaultValue = "0")]
    public int Step { get; set; }

    [SugarColumn(ColumnName = "Channel", IsNullable = false, DefaultValue = "0")]
    public CrmOutreachChannel Channel { get; set; }

    [SugarColumn(ColumnName = "Rendered_Text", ColumnDataType = StaticConfig.CodeFirst_BigString, IsNullable = false)]
    public string RenderedText { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Content_Hash", Length = 64, IsNullable = false)]
    public string ContentHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmOutreachDraftStatus Status { get; set; }

    [SugarColumn(ColumnName = "Approved_By", IsNullable = true)]
    public long? ApprovedBy { get; set; }

    [SugarColumn(ColumnName = "Approved_At", IsNullable = true)]
    public DateTimeOffset? ApprovedAt { get; set; }

    [SugarColumn(ColumnName = "Rejection_Reason", Length = 500, IsNullable = true)]
    public string? RejectionReason { get; set; }
}
