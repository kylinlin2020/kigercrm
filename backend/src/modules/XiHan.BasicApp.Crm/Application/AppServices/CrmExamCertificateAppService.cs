using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Security.Users;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>
/// Exam certificates. Issuance is explicit and only from a passed attempt; the verification
/// hash is computed once from immutable data and never changes, including on revocation.
/// </summary>
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services", Tag = "ExamCertificate")]
public sealed class CrmExamCertificateAppService(
    ICrmExamCertificateRepository certificates,
    ICrmExamAttemptRepository attempts,
    ICurrentUser currentUser)
    : CrmApplicationService, ICrmExamCertificateAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Execute)]
    public async Task<CrmExamCertificateDto> IssueAsync(
        long attemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken)
            ?? throw new InvalidOperationException("Exam attempt does not exist.");
        var existing = await certificates.GetByAttemptAsync(attemptId, cancellationToken);
        var gate = CrmExamCertificatePolicy.CanIssue(attempt.Status, existing is not null);
        if (!gate.Allowed) throw new InvalidOperationException($"Certificate cannot be issued: {gate.Reason}.");

        var issuedAt = DateTimeOffset.UtcNow;
        var validUntil = issuedAt.Add(CrmExamCertificatePolicy.Validity);
        var number = CrmExamCertificatePolicy.BuildNumber(currentUser.TenantId ?? 0, attemptId, issuedAt);
        var created = await certificates.AddAsync(new SysCrmExamCertificate
        {
            CertificateNo = number,
            AttemptId = attempt.BasicId,
            PaperId = attempt.PaperId,
            HolderUserId = attempt.UserId,
            Score = attempt.Score,
            TotalScore = attempt.TotalScore,
            IssuedAtUtc = issuedAt,
            ValidUntilUtc = validUntil,
            VerificationHash = CrmExamCertificatePolicy.BuildHash(
                number, attemptId, attempt.UserId, attempt.Score, attempt.TotalScore, issuedAt, validUntil),
            Status = CrmExamCertificateStatus.Active
        }, cancellationToken);
        return ToDto(created);
    }

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<IReadOnlyList<CrmExamCertificateDto>> GetMineAsync(CancellationToken cancellationToken = default)
        => (await certificates.GetByHolderAsync(RequireUserId(), cancellationToken)).Select(ToDto).ToArray();

    [PermissionAuthorize(CrmPermissionCodes.Read)]
    public async Task<CrmExamCertificateVerdictDto> VerifyAsync(
        CrmExamCertificateVerifyDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.CertificateNo) || string.IsNullOrWhiteSpace(input.VerificationHash))
            throw new ArgumentException("Certificate number and verification hash are required.", nameof(input));
        var certificate = await certificates.GetByNumberAsync(input.CertificateNo, cancellationToken);
        if (certificate is null)
            return new CrmExamCertificateVerdictDto { Valid = false, Reason = "not_found" };
        var verdict = CrmExamCertificatePolicy.Verify(certificate, input.VerificationHash, DateTimeOffset.UtcNow);
        return new CrmExamCertificateVerdictDto
        {
            Valid = verdict.Valid,
            Reason = verdict.Reason,
            Certificate = ToDto(certificate)
        };
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(CrmPermissionCodes.Update)]
    public async Task<CrmExamCertificateDto> RevokeAsync(
        CrmExamCertificateRevokeDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Reason))
            throw new ArgumentException("A revocation reason is required.", nameof(input));
        var certificate = await certificates.GetByIdAsync(input.CertificateId, cancellationToken)
            ?? throw new InvalidOperationException("Certificate does not exist.");
        if (certificate.Status == CrmExamCertificateStatus.Revoked)
            throw new InvalidOperationException("Certificate is already revoked.");
        certificate.Status = CrmExamCertificateStatus.Revoked;
        certificate.RevokedAtUtc = DateTimeOffset.UtcNow;
        certificate.RevokeReason = input.Reason.Trim();
        return ToDto(await certificates.UpdateAsync(certificate, cancellationToken));
    }

    private long RequireUserId()
        => currentUser.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    private static CrmExamCertificateDto ToDto(SysCrmExamCertificate entity) => new()
    {
        BasicId = entity.BasicId,
        CertificateNo = entity.CertificateNo,
        AttemptId = entity.AttemptId,
        PaperId = entity.PaperId,
        HolderUserId = entity.HolderUserId,
        Score = entity.Score,
        TotalScore = entity.TotalScore,
        IssuedAtUtc = entity.IssuedAtUtc,
        ValidUntilUtc = entity.ValidUntilUtc,
        VerificationHash = entity.VerificationHash,
        Status = (int)entity.Status
    };
}
