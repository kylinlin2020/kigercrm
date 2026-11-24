// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.AI.Application.AppServices;
using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Enums;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Knowledge ACL: public vs restricted documents, role allowlist and fail-closed default.</summary>
public sealed class KnowledgeAccessTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=kiger-ai-acl-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly KnowledgeAccessAppService _service;
    private readonly KnowledgeDocumentRepository _documents;

    public KnowledgeAccessTests()
    {
        _tenantScope = _tenant.Change(0);
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
        _db.CodeFirst.InitTables(typeof(SysKnowledgeDocument));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        _documents = new KnowledgeDocumentRepository(_resolver.Object);
        _service = new KnowledgeAccessAppService(_documents);
    }

    [Fact]
    public void Policy_ShouldFailClosedForRestrictedWithoutMatchingRole()
    {
        var publicDoc = new SysKnowledgeDocument { Visibility = KnowledgeVisibility.Public };
        var restricted = new SysKnowledgeDocument
        {
            Visibility = KnowledgeVisibility.Restricted,
            AllowedRoles = " sales , Sales, legal"
        };
        Assert.Equal(new[] { "legal", "sales" }, KnowledgeAccessPolicy.ParseRoles(restricted.AllowedRoles));
        Assert.Equal("legal,sales", KnowledgeAccessPolicy.NormalizeRoles(["sales", "legal", "Sales"]));
        Assert.True(KnowledgeAccessPolicy.CanView(publicDoc, _ => false));
        Assert.True(KnowledgeAccessPolicy.CanView(restricted,
            role => string.Equals(role, "SALES", StringComparison.OrdinalIgnoreCase)));
        Assert.True(KnowledgeAccessPolicy.CanView(restricted, role => role == "super_admin"));
        Assert.False(KnowledgeAccessPolicy.CanView(restricted, role => role == "marketing"));
        var noRoles = new SysKnowledgeDocument { Visibility = KnowledgeVisibility.Restricted };
        Assert.False(KnowledgeAccessPolicy.CanView(noRoles, _ => false));
        Assert.True(KnowledgeAccessPolicy.CanView(noRoles, role => role == "super_admin"));
    }

    [Fact]
    public async Task Flow_ShouldUpdateAccessAndClearRolesOnPublic()
    {
        var document = await _documents.AddAsync(new SysKnowledgeDocument
        {
            TenantId = 0,
            Title = "secret",
            RawContent = "secret content"
        });

        var restricted = await _service.UpdateAsync(new KnowledgeAccessUpdateDto
        {
            DocumentId = document.BasicId,
            Visibility = (int)KnowledgeVisibility.Restricted,
            AllowedRoles = ["sales", "legal", "sales"]
        });
        Assert.Equal((int)KnowledgeVisibility.Restricted, restricted.Visibility);
        Assert.Equal(new[] { "legal", "sales" }, restricted.AllowedRoles);
        var fetched = await _service.GetAsync(document.BasicId);
        Assert.Equal(restricted.Visibility, fetched.Visibility);
        Assert.Equal(restricted.AllowedRoles, fetched.AllowedRoles);

        var backToPublic = await _service.UpdateAsync(new KnowledgeAccessUpdateDto
        {
            DocumentId = document.BasicId,
            Visibility = (int)KnowledgeVisibility.Public,
            AllowedRoles = ["sales"]
        });
        Assert.Empty(backToPublic.AllowedRoles);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetAsync(99999));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.UpdateAsync(new KnowledgeAccessUpdateDto
        {
            DocumentId = document.BasicId,
            Visibility = 99
        }));
    }

    [Fact]
    public void Contract_ShouldGuardPermissionsAndMigration()
    {
        var service = Read("backend", "src", "modules", "XiHan.BasicApp.AI", "Application",
            "AppServices", "KnowledgeAccessAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.60", "5.9.60.sql");
        Assert.Contains("Tag = \"KnowledgeAccess\"", service, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(KnowledgePermissionCodes.Update)]", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS visibility", migration, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS allowed_roles", migration, StringComparison.Ordinal);
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