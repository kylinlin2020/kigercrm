// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Enums;

namespace XiHan.BasicApp.AI.Domain.DomainServices;

/// <summary>
/// 知识文档摄取命令
/// </summary>
public sealed record KnowledgeIngestCommand(
    string Title,
    KnowledgeSourceType SourceType,
    string? Source,
    string Text,
    string? EmbeddingProviderCode,
    string? Remark);

/// <summary>
/// 知识文档命令结果
/// </summary>
public sealed record KnowledgeDocumentCommandResult(SysKnowledgeDocument Document);

/// <summary>Incremental sync outcome: Rebuilt=false means content was unchanged and no I/O ran.</summary>
public sealed record KnowledgeDocumentSyncResult(SysKnowledgeDocument Document, bool Rebuilt);
