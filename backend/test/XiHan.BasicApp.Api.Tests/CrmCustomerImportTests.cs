// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Crm.Application.AppServices;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Crm.Domain.DomainServices;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Prospecting;
using XiHan.BasicApp.Crm.Infrastructure.Repositories;
using XiHan.BasicApp.Saas.Application.Contracts;
using XiHan.BasicApp.Saas.Application.Dtos;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Customer import: field mapping, zero-write preview, de-duplication and audit trail.</summary>
public sealed class CrmCustomerImportTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-cust-import-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly Mock<IImportHistoryAppService> _history = new();
    private readonly CrmCustomerImportAppService _service;

    public CrmCustomerImportTests()
    {
        _tenantScope = _tenant.Change(17);
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
        _db.CodeFirst.InitTables(typeof(SysCrmCustomer));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var customers = new CrmCustomerRepository(_resolver.Object);
        var domain = new Mock<ICrmCustomerDomainService>();
        domain.Setup(service => service.CreateAsync(
                It.IsAny<CrmCustomerCreateCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrmCustomerCreateCommand command, CancellationToken _) =>
                customers.AddAsync(new SysCrmCustomer { Name = command.Name, Website = command.Website }).GetAwaiter().GetResult());
        _history.Setup(service => service.CreateAsync(
                It.IsAny<ImportHistoryCreateDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportHistoryDto { BasicId = 555 });
        _service = new CrmCustomerImportAppService(customers, domain.Object, _history.Object);
    }

    [Fact]
    public void Policy_ShouldApplyMappingValidateAndNormalise()
    {
        var mapping = new Dictionary<string, string>
        {
            ["company"] = "name",
            ["site"] = "website",
            ["region"] = "countryRegion",
            ["sector"] = "industry"
        };
        var rows = CrmCustomerImportPolicy.ParseRows("""
            [{"company":"  Acme  GmbH ","site":"www.acme.com","region":"de","sector":"Pumps"}]
            """, mapping);
        var row = Assert.Single(rows);
        Assert.Empty(row.Errors);
        Assert.Equal("ACME GMBH", row.NameKey);
        Assert.Equal("acme.com", row.WebsiteHost);
        Assert.Equal("DE", row.CountryRegion);
        Assert.Equal("Import", row.Source);

        Assert.Throws<ArgumentException>(() => CrmCustomerImportPolicy.ParseRows("[]", mapping));
        Assert.Throws<ArgumentException>(() => CrmCustomerImportPolicy.ParseRows("[]", new Dictionary<string, string> { ["x"] = "nope" }));
        Assert.Throws<ArgumentException>(() => CrmCustomerImportPolicy.ParseRows("[]", new Dictionary<string, string> { ["x"] = "website" }));
        var invalid = CrmCustomerImportPolicy.ParseRows("[{\"company\":\"\"}]", mapping);
        Assert.Contains("invalid_name", invalid[0].Errors);
    }

    [Fact]
    public async Task ImportFlow_ShouldCreateOnlyValidRowsAndWriteHistory()
    {
        var input = new CrmCustomerImportInputDto
        {
            RowsJson = """
                [
                  {"company":"Acme GmbH","site":"www.acme.com","region":"DE"},
                  {"company":" acme   gmbh ","region":"DE"},
                  {"company":"Beta Ltd","region":"US"}
                ]
                """,
            Mapping = new Dictionary<string, string>
            {
                ["company"] = "name",
                ["site"] = "website",
                ["region"] = "countryRegion"
            },
            FileName = "customers.json"
        };

        var preview = await _service.PreviewAsync(input);
        Assert.Equal(3, preview.Total);
        Assert.Equal(3, preview.Valid);
        Assert.Equal(1, preview.Duplicates);
        Assert.Empty(await _db.Queryable<SysCrmCustomer>().ToListAsync());

        var imported = await _service.ImportAsync(input);
        Assert.Equal(3, imported.Total);
        Assert.Equal(2, imported.Created);
        Assert.Equal(1, imported.Duplicates);
        Assert.Equal(555, imported.ImportHistoryId);
        Assert.Equal(2, (await _db.Queryable<SysCrmCustomer>().ToListAsync()).Count);
        _history.Verify(service => service.CreateAsync(
            It.Is<ImportHistoryCreateDto>(dto => dto.PageCode == "crm.customer-import"
                && dto.SuccessCount == 2 && dto.FailCount == 1 && dto.FileName == "customers.json"),
            It.IsAny<CancellationToken>()), Times.Once);

        var replay = await _service.ImportAsync(input);
        Assert.Equal(0, replay.Created);
        Assert.Equal(3, replay.Duplicates);
        Assert.Equal(2, (await _db.Queryable<SysCrmCustomer>().ToListAsync()).Count);
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndAudit()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.Crm", "Application",
            "AppServices", "CrmCustomerImportAppService.cs");
        Assert.Contains("Tag = \"CustomerImport\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(CrmPermissionCodes.Create)]", service, StringComparison.Ordinal);
        Assert.Contains("ImportHistoryCreateDto", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
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