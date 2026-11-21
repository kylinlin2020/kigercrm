namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmExamCertificateDto
{
    public long BasicId { get; set; }
    public string CertificateNo { get; set; } = string.Empty;
    public long AttemptId { get; set; }
    public long PaperId { get; set; }
    public long HolderUserId { get; set; }
    public int Score { get; set; }
    public int TotalScore { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ValidUntilUtc { get; set; }
    public string VerificationHash { get; set; } = string.Empty;
    public int Status { get; set; }
}

public sealed class CrmExamCertificateVerifyDto
{
    public string CertificateNo { get; set; } = string.Empty;
    public string VerificationHash { get; set; } = string.Empty;
}

public sealed class CrmExamCertificateVerdictDto
{
    public bool Valid { get; set; }
    public string Reason { get; set; } = string.Empty;
    public CrmExamCertificateDto? Certificate { get; set; }
}

public sealed class CrmExamCertificateRevokeDto
{
    public long CertificateId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
