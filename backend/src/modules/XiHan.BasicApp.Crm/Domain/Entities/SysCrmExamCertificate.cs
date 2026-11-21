using SqlSugar;
using XiHan.BasicApp.Core.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.Framework.MultiTenancy;

namespace XiHan.BasicApp.Crm.Domain.Entities;

/// <summary>
/// Certificate issued for a passed exam attempt. The verification hash is derived from immutable
/// certificate data; revocation and expiry are checked separately (never by mutating the hash).
/// </summary>
[SugarTable(TableName = "Crm_Exam_Certificate", TableDescription = "CRM exam certificates")]
[SugarIndex("IX_{table}_Te_Us", nameof(TenantId), OrderByType.Asc, nameof(HolderUserId), OrderByType.Asc)]
[SugarIndex("UX_{table}_Te_No_IsDe", nameof(TenantId), OrderByType.Asc, nameof(CertificateNo), OrderByType.Asc, nameof(IsDeleted), OrderByType.Asc, true)]
public sealed class SysCrmExamCertificate : BasicAppFullAuditedEntity, IStrictMultiTenantEntity
{
    [SugarColumn(ColumnName = "Certificate_No", Length = 80, IsNullable = false)]
    public string CertificateNo { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Attempt_Id", IsNullable = false)]
    public long AttemptId { get; set; }

    [SugarColumn(ColumnName = "Paper_Id", IsNullable = false)]
    public long PaperId { get; set; }

    [SugarColumn(ColumnName = "Holder_User_Id", IsNullable = false)]
    public long HolderUserId { get; set; }

    [SugarColumn(ColumnName = "Score", IsNullable = false)]
    public int Score { get; set; }

    [SugarColumn(ColumnName = "Total_Score", IsNullable = false)]
    public int TotalScore { get; set; }

    [SugarColumn(ColumnName = "Issued_At_Utc", IsNullable = false)]
    public DateTimeOffset IssuedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Valid_Until_Utc", IsNullable = false)]
    public DateTimeOffset ValidUntilUtc { get; set; }

    [SugarColumn(ColumnName = "Verification_Hash", Length = 64, IsNullable = false)]
    public string VerificationHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "Status", IsNullable = false, DefaultValue = "0")]
    public CrmExamCertificateStatus Status { get; set; }

    [SugarColumn(ColumnName = "Revoked_At_Utc", IsNullable = true)]
    public DateTimeOffset? RevokedAtUtc { get; set; }

    [SugarColumn(ColumnName = "Revoke_Reason", Length = 500, IsNullable = true)]
    public string? RevokeReason { get; set; }
}
