// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XiHan.BasicApp.AI.Domain.DomainServices.Implementations;
using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Enums;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Domain.Repositories;
using XiHan.Framework.AI.Abstractions.Rag;

namespace XiHan.BasicApp.Api.Tests;

/// <summary>Incremental re-index: skip unchanged documents, rebuild changed/failed ones.</summary>
public sealed class KnowledgeIncrementalIndexTests
{
    private const long DocumentId = 100;
    private const string Content = "hello world";

    [Fact]
    public void Policy_ShouldSkipOnlyIndexedUnchangedDocuments()
    {
        var hash = KnowledgeTagPolicy.HashContent(Content);
        var unchanged = MakeDocument(hash, hash, KnowledgeIndexStatus.Indexed, 3);
        Assert.False(KnowledgeIndexPolicy.NeedsReindex(unchanged));

        var changed = MakeDocument(hash, KnowledgeTagPolicy.HashContent("changed"), KnowledgeIndexStatus.Indexed, 3);
        Assert.True(KnowledgeIndexPolicy.NeedsReindex(changed));

        var failed = MakeDocument(hash, hash, KnowledgeIndexStatus.Failed, 0);
        Assert.True(KnowledgeIndexPolicy.NeedsReindex(failed));

        var pending = MakeDocument(hash, hash, KnowledgeIndexStatus.Pending, 0);
        Assert.True(KnowledgeIndexPolicy.NeedsReindex(pending));

        var noHash = MakeDocument(null, null, KnowledgeIndexStatus.Indexed, 3);
        Assert.True(KnowledgeIndexPolicy.NeedsReindex(noHash));

        var zeroChunks = MakeDocument(hash, hash, KnowledgeIndexStatus.Indexed, 0);
        Assert.True(KnowledgeIndexPolicy.NeedsReindex(zeroChunks));

        KnowledgeIndexPolicy.MarkIndexed(changed);
        Assert.Equal(changed.ContentHash, changed.IndexedContentHash);
        KnowledgeIndexPolicy.MarkFailed(changed);
        Assert.Null(changed.IndexedContentHash);
    }

    [Fact]
    public async Task Sync_ShouldSkipUnchangedAndRebuildChanged()
    {
        var hash = KnowledgeTagPolicy.HashContent(Content);
        var document = MakeDocument(hash, hash, KnowledgeIndexStatus.Indexed, 3);
        var repository = new Mock<IKnowledgeDocumentRepository>(MockBehavior.Strict);
        repository.Setup(item => item.GetByIdAsync(DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        repository.Setup(item => item.UpdateAsync(It.IsAny<SysKnowledgeDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SysKnowledgeDocument entity, CancellationToken _) => entity);
        var ingestor = new Mock<IKnowledgeIngestor>(MockBehavior.Strict);
        var service = new KnowledgeDocumentDomainService(repository.Object, ingestor.Object, NullLogger<KnowledgeDocumentDomainService>.Instance);

        // Unchanged: no vector-store call at all.
        var skipped = await service.SyncAsync(DocumentId);
        Assert.False(skipped.Rebuilt);
        ingestor.Verify(item => item.RemoveDocumentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        ingestor.Verify(item => item.IngestAsync(It.IsAny<KnowledgeIngestRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        // Changed: remove old vectors + re-ingest, and record the new indexed hash.
        document.RawContent = "changed content";
        document.ContentHash = KnowledgeTagPolicy.HashContent(document.RawContent);
        ingestor.Setup(item => item.RemoveDocumentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ingestor.Setup(item => item.IngestAsync(It.IsAny<KnowledgeIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var rebuilt = await service.SyncAsync(DocumentId);
        Assert.True(rebuilt.Rebuilt);
        Assert.Equal(4, document.ChunkCount);
        Assert.Equal(KnowledgeIndexStatus.Indexed, document.Status);
        Assert.Equal(document.ContentHash, document.IndexedContentHash);
        ingestor.Verify(item => item.RemoveDocumentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        ingestor.Verify(item => item.IngestAsync(It.IsAny<KnowledgeIngestRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // After rebuild, a second sync is a no-op.
        var again = await service.SyncAsync(DocumentId);
        Assert.False(again.Rebuilt);
        ingestor.Verify(item => item.IngestAsync(It.IsAny<KnowledgeIngestRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Contract_ShouldGuardMigrationAndPolicy()
    {
        var migration = Read("backend", "src", "main", "XiHan.BasicApp.WebHost",
            "UpdateScripts", "5.9.58", "5.9.58.sql");
        var contract = Read("backend", "src", "modules", "XiHan.BasicApp.AI",
            "Application", "Contracts", "KnowledgeContracts.cs");
        Assert.Contains("ALTER TABLE sys_knowledge_document ADD COLUMN IF NOT EXISTS indexed_content_hash", migration, StringComparison.Ordinal);
        Assert.Contains("Task<KnowledgeSyncResultDto> SyncAsync", contract, StringComparison.Ordinal);
    }

    private static SysKnowledgeDocument MakeDocument(string? contentHash, string? indexedHash, KnowledgeIndexStatus status, int chunks)
    {
        var document = new SysKnowledgeDocument
        {
            Title = "doc",
            RawContent = Content,
            ContentHash = contentHash,
            IndexedContentHash = indexedHash,
            Status = status,
            ChunkCount = chunks
        };
        typeof(SysKnowledgeDocument).GetProperty(nameof(SysKnowledgeDocument.BasicId))!
            .SetValue(document, DocumentId);
        return document;
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(RepositoryRoot(), Path.Combine(segments))));

    private static string RepositoryRoot([CallerFilePath] string filePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));
}
