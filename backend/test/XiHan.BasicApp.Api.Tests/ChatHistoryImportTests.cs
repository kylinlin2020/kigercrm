// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using XiHan.BasicApp.Chat.Application.AppServices;
using XiHan.BasicApp.Chat.Application.Dtos;
using XiHan.BasicApp.Chat.Domain.Entities;
using XiHan.BasicApp.Chat.Domain.Entities.Enums;
using XiHan.BasicApp.Chat.Domain.Prospecting;
using XiHan.BasicApp.Chat.Infrastructure.Repositories;
using XiHan.Framework.Data.SqlSugar.Clients;
using XiHan.Framework.Data.SqlSugar.Extensions;
using XiHan.Framework.Domain.Entities.Abstracts;
using XiHan.Framework.MultiTenancy;
using XiHan.Framework.MultiTenancy.Abstractions;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>M4 history import: pure preview, auditable import and replay-safe dedupe.</summary>
public sealed class ChatHistoryImportTests : IDisposable
{
    private const long TenantId = 9991;

    private readonly string _connectionString = $"Data Source=kiger-chat-import-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private readonly SqlSugarScope _db;
    private readonly CurrentTenant _tenant = new(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly IDisposable _tenantScope;
    private readonly Mock<ISqlSugarClientResolver> _resolver = new();
    private readonly ChatHistoryImportAppService _service;

    public ChatHistoryImportTests()
    {
        _tenantScope = _tenant.Change(TenantId);
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
        _db.CodeFirst.InitTables(typeof(SysChatHistoryImportBatch), typeof(SysChatHistoryImportRow));
        _resolver.Setup(value => value.GetClientForEntity(It.IsAny<Type>())).Returns(_db);
        var batches = new ChatHistoryImportBatchRepository(_resolver.Object);
        var rows = new ChatHistoryImportRowRepository(_resolver.Object);
        _service = new ChatHistoryImportAppService(batches, rows);
    }

    [Fact]
    public void Policy_ShouldValidateDedupeAndHashWithoutAnyWrite()
    {
        var rows = new[]
        {
            new ChatHistoryImportSourceRow("m1", "alice", "bob", "hello", "2026-11-18T10:00:00Z"),
            new ChatHistoryImportSourceRow("m1", "alice", "bob", "hello again", "2026-11-18T10:01:00Z"),
            new ChatHistoryImportSourceRow("m2", "", "bob", "hello", "2026-11-18T10:02:00Z"),
            new ChatHistoryImportSourceRow("m3", "alice", "bob", "hello", "not-a-date"),
            new ChatHistoryImportSourceRow("m4", "alice", "bob", "hello", "2026-11-18T10:04:00Z"),
        };
        var verdicts = ChatHistoryImportPolicy.Evaluate(rows, externalId => externalId == "m4");

        Assert.Equal(ChatHistoryImportRowStatus.Imported, verdicts[0].Status);
        Assert.Equal(ChatHistoryImportRowStatus.Duplicate, verdicts[1].Status);
        Assert.Equal("duplicate_in_file", verdicts[1].Reason);
        Assert.Equal(ChatHistoryImportRowStatus.Invalid, verdicts[2].Status);
        Assert.Equal(ChatHistoryImportRowStatus.Invalid, verdicts[3].Status);
        Assert.Equal(ChatHistoryImportRowStatus.Duplicate, verdicts[4].Status);
        Assert.Equal(64, verdicts[0].ContentHash.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatHistoryImportPolicy.Evaluate(
            Enumerable.Range(0, ChatHistoryImportPolicy.MaximumRowsPerBatch + 1)
                .Select(index => new ChatHistoryImportSourceRow($"m{index}", "a", "b", "c", "2026-11-18T10:00:00Z"))
                .ToArray(), _ => false));
    }

    [Fact]
    public async Task Import_ShouldStageAuditableRowsAndReplayAsDuplicates()
    {
        var input = Input();

        var preview = await _service.PreviewAsync(input);
        Assert.Equal(0, preview.BatchId);
        Assert.Equal(2, preview.Imported);
        Assert.Equal(0, await _db.Queryable<SysChatHistoryImportBatch>().ClearFilter().CountAsync());

        var imported = await _service.ImportAsync(input);
        Assert.True(imported.BatchId > 0);
        Assert.Equal(2, imported.Imported);
        Assert.Equal(1, imported.Invalid);
        // All three verdict rows are staged for audit, including the invalid one.
        Assert.Equal(3, await _db.Queryable<SysChatHistoryImportRow>().ClearFilter().CountAsync());
        Assert.Equal(1, await _db.Queryable<SysChatHistoryImportBatch>().ClearFilter().CountAsync());

        var replay = await _service.ImportAsync(input);
        Assert.Equal(0, replay.Imported);
        Assert.Equal(2, replay.Duplicates);
        Assert.Equal(6, await _db.Queryable<SysChatHistoryImportRow>().ClearFilter().CountAsync());

        Assert.Equal(2, (await _service.GetBatchesAsync()).Count);
        Assert.Equal(6, (await _service.GetRowsAsync(imported.BatchId)).Count
            + (await _service.GetRowsAsync(replay.BatchId)).Count);
    }

    [Fact]
    public void ImportContract_ShouldCarryPermissionsAndStayReviewOnly()
    {
        var app = Read("backend", "src", "modules", "XiHan.BasicApp.Chat",
            "Application", "AppServices", "ChatHistoryImportAppService.cs");
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.48", "5.9.48.sql");
        Assert.Contains("[PermissionAuthorize(ChatPermissionCodes.Read)]\n    public async Task<ChatHistoryImportResultDto> PreviewAsync", app, StringComparison.Ordinal);
        Assert.Contains("[PermissionAuthorize(ChatPermissionCodes.Manage)]\n    public async Task<ChatHistoryImportResultDto> ImportAsync", app, StringComparison.Ordinal);
        Assert.DoesNotContain("SysChatMessage", app, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS sys_chat_history_import_row", migration, StringComparison.Ordinal);
    }

    private static ChatHistoryImportInputDto Input() => new()
    {
        FileName = "history.json",
        SourceChannel = "import-preview",
        Rows =
        [
            new ChatHistoryImportRowDto { ExternalId = "m1", Sender = "alice", Recipient = "bob", Content = "hello", SentAt = "2026-11-18T10:00:00Z" },
            new ChatHistoryImportRowDto { ExternalId = "m2", Sender = "alice", Recipient = "bob", Content = "world", SentAt = "2026-11-18T10:01:00Z" },
            new ChatHistoryImportRowDto { ExternalId = "m3", Sender = "", Recipient = "bob", Content = "bad", SentAt = "2026-11-18T10:02:00Z" },
        ]
    };

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
