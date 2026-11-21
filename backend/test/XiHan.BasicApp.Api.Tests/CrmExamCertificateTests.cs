// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Exam certificates: issue on pass, stable hash, verification and revocation.</summary>
public sealed class CrmExamCertificateTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-cert-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly CrmExamCertificateAppService _service;
    private readonly CrmExamAttemptRepository _attempts;
    private readonly CrmExamCertificateRepository _certificates;

    public CrmExamCertificateTests()
    {
        _tenantScope = _tenant.Change(31);
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        var tenant = _tenant;
        _db = new SqlSugarScope(
            new ConnectionConfig
            {
                DbType = DbType.Sqlite,
                ConnectionString = _connectionString,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            },
            db =>
            {
                db.QueryFilter.AddTableFilter<IStrictMultiTenantEntity>(entity => ScopeId() == entity.TenantId);
                db.Aop.DataExecuting = (_, entityInfo) =>
                {
                    if (entityInfo.OperationType == DataFilterType.InsertByObject)
                    {
                        entityInfo.TrySetSnowflakeId(SnowFlakeSingle.Instance.NextId());
                        entityInfo.ToCreated(EntityAuditContext.From(null, tenant.Id));
                    }
                    // SQLite stores DateTimeOffset as wall-clock text and reads it back in the local
                    // zone; normalising to local on insert preserves the instant (PostgreSQL timestamptz
                    // does not need this).
                    if (entityInfo.EntityValue is SysCrmExamCertificate certificate)
                    {
                        certificate.IssuedAtUtc = certificate.IssuedAtUtc.ToLocalTime();
                        certificate.ValidUntilUtc = certificate.ValidUntilUtc.ToLocalTime();
                        if (certificate.RevokedAtUtc.HasValue)
                            certificate.RevokedAtUtc = certificate.RevokedAtUtc.Value.ToLocalTime();
                    }
                };
            });
        _db.CodeFirst.InitTables(typeof(SysCrmExamAttempt), typeof(SysCrmExamCertificate));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var user = new Mock<ICurrentUser>();
        user.SetupGet(value => value.UserId).Returns(42);
        user.SetupGet(value => value.TenantId).Returns(31);
        _attempts = new CrmExamAttemptRepository(_resolver.Object);
        _certificates = new CrmExamCertificateRepository(_resolver.Object);
        _service = new CrmExamCertificateAppService(_certificates, _attempts, user.Object);
    }

    [Fact]
    public void Policy_ShouldBuildStableHashAndCheckRevocationAndExpiry()
    {
        var issued = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var validUntil = issued.Add(CrmExamCertificatePolicy.Validity);
        var number = CrmExamCertificatePolicy.BuildNumber(31, 99, issued);
        var hash = CrmExamCertificatePolicy.BuildHash(number, 99, 42, 80, 100, issued, validUntil);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, CrmExamCertificatePolicy.BuildHash(number, 99, 42, 80, 100, issued, validUntil));
        // PostgreSQL timestamptz stores microseconds and a different offset after round-trip;
        // second-level normalisation must keep the hash stable.
        var rounded = issued.ToOffset(TimeSpan.FromHours(8)).AddTicks(1234);
        Assert.Equal(hash, CrmExamCertificatePolicy.BuildHash(number, 99, 42, 80, 100, rounded, validUntil));

        var certificate = new SysCrmExamCertificate
        {
            CertificateNo = number,
            AttemptId = 99,
            HolderUserId = 42,
            Score = 80,
            TotalScore = 100,
            IssuedAtUtc = issued,
            ValidUntilUtc = validUntil,
            VerificationHash = hash,
            Status = CrmExamCertificateStatus.Active
        };
        Assert.True(CrmExamCertificatePolicy.Verify(certificate, hash, issued.AddDays(1)).Valid);
        Assert.Equal("hash_mismatch", CrmExamCertificatePolicy.Verify(certificate, "bad", issued).Reason);
        Assert.Equal("expired", CrmExamCertificatePolicy.Verify(certificate, hash, validUntil.AddDays(1)).Reason);
        certificate.Status = CrmExamCertificateStatus.Revoked;
        Assert.Equal("revoked", CrmExamCertificatePolicy.Verify(certificate, hash, issued).Reason);
        Assert.False(CrmExamCertificatePolicy.CanIssue(CrmExamAttemptStatus.Failed, false).Allowed);
        Assert.False(CrmExamCertificatePolicy.CanIssue(CrmExamAttemptStatus.Passed, true).Allowed);
    }

    [Fact]
    public async Task Flow_ShouldIssueOnceVerifyAndRevoke()
    {
        var passed = await _attempts.AddAsync(new SysCrmExamAttempt
        {
            PaperId = 7,
            UserId = 42,
            Status = CrmExamAttemptStatus.Passed,
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            Score = 80,
            TotalScore = 100
        });
        var failed = await _attempts.AddAsync(new SysCrmExamAttempt
        {
            PaperId = 7,
            UserId = 42,
            Status = CrmExamAttemptStatus.Failed,
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.IssueAsync(failed.BasicId));
        var issued = await _service.IssueAsync(passed.BasicId);
        Assert.Equal(80, issued.Score);
        Assert.Equal(42, issued.HolderUserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.IssueAsync(passed.BasicId));
        Assert.Single(await _service.GetMineAsync());

        var ok = await _service.VerifyAsync(new CrmExamCertificateVerifyDto
        {
            CertificateNo = issued.CertificateNo,
            VerificationHash = issued.VerificationHash
        });
        Assert.True(ok.Valid);
        Assert.Equal("valid", ok.Reason);
        var bad = await _service.VerifyAsync(new CrmExamCertificateVerifyDto
        {
            CertificateNo = issued.CertificateNo,
            VerificationHash = "wrong"
        });
        Assert.False(bad.Valid);
        Assert.Equal("hash_mismatch", bad.Reason);
        var missing = await _service.VerifyAsync(new CrmExamCertificateVerifyDto
        {
            CertificateNo = "NOPE",
            VerificationHash = "x"
        });
        Assert.Equal("not_found", missing.Reason);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.RevokeAsync(
            new CrmExamCertificateRevokeDto { CertificateId = issued.BasicId }));
        var revoked = await _service.RevokeAsync(new CrmExamCertificateRevokeDto
        {
            CertificateId = issued.BasicId,
            Reason = "fraud"
        });
        Assert.Equal((int)CrmExamCertificateStatus.Revoked, revoked.Status);
        var afterRevoke = await _service.VerifyAsync(new CrmExamCertificateVerifyDto
        {
            CertificateNo = issued.CertificateNo,
            VerificationHash = issued.VerificationHash
        });
        Assert.False(afterRevoke.Valid);
        Assert.Equal("revoked", afterRevoke.Reason);
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndNoExternalCalls()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmExamCertificateAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.56", "5.9.56.sql");
        Assert.Contains("Tag = \"ExamCertificate\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Update)]", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_exam_certificate", migration, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _tenantScope.Dispose();
        AsyncLocalCurrentTenantAccessor.Instance.Current = null;
        _db.Dispose();
        _keepAlive.Dispose();
    }

    private static long ScopeId() => AsyncLocalCurrentTenantAccessor.Instance.Current?.TenantId ?? 0;

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
