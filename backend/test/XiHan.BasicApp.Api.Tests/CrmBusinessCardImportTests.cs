// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices.Implementations;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Domain.Repositories;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Business-card OCR import: deterministic parsing, zero-write preview, explicit Lead creation.</summary>
public sealed class CrmBusinessCardImportTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-card-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly CrmBusinessCardImportAppService _service;

    public CrmBusinessCardImportTests()
    {
        _tenantScope = _tenant.Change(11);
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
                };
            });
        _db.CodeFirst.InitTables(typeof(SysCrmBusinessCardImportRow), typeof(SysCrmLead));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var leads = new CrmLeadRepository(_resolver.Object);
        var leadDomain = new CrmLeadDomainService(
            leads,
            Mock.Of<ICrmCustomerRepository>(),
            Mock.Of<ICrmContactRepository>());
        _service = new CrmBusinessCardImportAppService(
            new CrmBusinessCardImportRepository(_resolver.Object),
            leads,
            leadDomain);
    }

    [Fact]
    public void Policy_ShouldParseNormaliseAndGateReview()
    {
        var rows = CrmBusinessCardPolicy.ParseRows("""
            [
              {"externalId":"a","contactName":" Zhang ","email":" Zhang@Example.COM ","phone":"+86 138-0013-8000","website":"www.example.com"},
              {"externalId":"b","companyName":"Acme"}
            ]
            """);
        Assert.Equal(2, rows.Count);
        Assert.Equal("zhang@example.com", rows[0].NormalizedEmail);
        Assert.Equal("8613800138000", rows[0].PhoneDigits);
        Assert.Equal("example.com", rows[0].WebsiteHost);
        Assert.Empty(rows[0].Errors);
        Assert.Contains("contact_required", rows[1].Errors);
        Assert.Throws<ArgumentException>(() => CrmBusinessCardPolicy.ParseRows("[]"));
        Assert.Throws<ArgumentException>(() => CrmBusinessCardPolicy.ParseRows("{}"));
        Assert.Throws<ArgumentException>(() => CrmBusinessCardPolicy.ParseRows(
            $"[{string.Join(',', Enumerable.Range(1, 201).Select(i => $"{{\"externalId\":\"{i}\",\"email\":\"a{i}@x.com\"}}"))}]"));

        Assert.False(CrmBusinessCardPolicy.CanConfirm(CrmBusinessCardImportStatus.Pending, "duplicate_lead_email").Allowed);
        Assert.False(CrmBusinessCardPolicy.CanConfirm(CrmBusinessCardImportStatus.Confirmed, null).Allowed);
        Assert.True(CrmBusinessCardPolicy.CanConfirm(CrmBusinessCardImportStatus.Pending, null).Allowed);
        Assert.False(CrmBusinessCardPolicy.CanReject(CrmBusinessCardImportStatus.Pending, null).Allowed);
        Assert.True(CrmBusinessCardPolicy.CanReject(CrmBusinessCardImportStatus.Pending, "blurry").Allowed);
    }

    [Fact]
    public async Task ImportFlow_ShouldPreviewWithoutWritesAndCreateLeadOnlyOnConfirm()
    {
        var payload = """
            [
              {"externalId":"a","contactName":"Alice","companyName":"Acme","email":"alice@acme.com","phone":"13800138000"},
              {"externalId":"b","contactName":"Bob","companyName":"Beta","email":"bob@beta.com"},
              {"externalId":"invalid","companyName":"No contact"}
            ]
            """;
        var input = new CrmBusinessCardImportInputDto { RowsJson = payload };

        var preview = await _service.PreviewAsync(input);
        Assert.Equal(3, preview.Total);
        Assert.Equal(2, preview.Valid);
        Assert.Empty(await _service.GetBatchesAsync());

        var imported = await _service.ImportAsync(input);
        Assert.Equal("Alice", (await _service.GetRowsAsync(imported.BatchId))[0].ContactName);
        Assert.Equal(2, imported.Staged);
        Assert.Equal(1, imported.Invalid);
        Assert.Equal(0, imported.DuplicateStaged);

        var rows = await _service.GetRowsAsync(imported.BatchId);
        Assert.All(rows, row => Assert.Equal((int)CrmBusinessCardImportStatus.Pending, row.Status));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RejectAsync(new CrmBusinessCardDecisionDto { RowId = rows[0].BasicId }));

        var confirmed = await _service.ConfirmAsync(new CrmBusinessCardDecisionDto { RowId = rows[0].BasicId });
        Assert.Equal((int)CrmBusinessCardImportStatus.Confirmed, confirmed.Status);
        Assert.NotNull(confirmed.ConvertedLeadId);
        var lead = await new CrmLeadRepository(_resolver.Object).GetByIdAsync(confirmed.ConvertedLeadId!.Value);
        Assert.NotNull(lead);
        Assert.Equal("BusinessCardOcr", lead!.Source);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ConfirmAsync(new CrmBusinessCardDecisionDto { RowId = rows[0].BasicId }));
        Assert.Single(await _db.Queryable<SysCrmLead>().ToListAsync());

        var rejected = await _service.RejectAsync(new CrmBusinessCardDecisionDto
        {
            RowId = rows[1].BasicId,
            Reason = "blurry image"
        });
        Assert.Equal((int)CrmBusinessCardImportStatus.Rejected, rejected.Status);
        Assert.Single(await _db.Queryable<SysCrmLead>().ToListAsync());
    }

    [Fact]
    public async Task Duplicates_ShouldBlockConfirmation()
    {
        var payload = """
            [
              {"externalId":"a","contactName":"Alice","email":"dup@example.com","phone":"13900139000"},
              {"externalId":"b","contactName":"Other","email":"dup@example.com"},
              {"externalId":"c","contactName":"Third","email":"third@example.com","phone":"13900139000"}
            ]
            """;
        var preview = await _service.PreviewAsync(new CrmBusinessCardImportInputDto { RowsJson = payload });
        Assert.Equal(2, preview.Duplicates);
        Assert.Contains("duplicate_in_batch", preview.Rows.First(row => row.ExternalId == "b").DuplicateReason);
        Assert.Contains("duplicate_in_batch", preview.Rows.First(row => row.ExternalId == "c").DuplicateReason);

        var imported = await _service.ImportAsync(new CrmBusinessCardImportInputDto { RowsJson = payload });
        Assert.Equal(2, imported.DuplicateStaged);
        var rows = await _service.GetRowsAsync(imported.BatchId);
        var duplicate = rows.First(row => row.ExternalId == "b");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ConfirmAsync(new CrmBusinessCardDecisionDto { RowId = duplicate.BasicId }));
        Assert.Empty(await _db.Queryable<SysCrmLead>().ToListAsync());
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndNoOcrClient()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmBusinessCardImportAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.53", "5.9.53.sql");
        Assert.Contains("Tag = \"BusinessCardImport\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Execute)]", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Tesseract", service, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS crm_business_card_import_row", migration, StringComparison.Ordinal);
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
