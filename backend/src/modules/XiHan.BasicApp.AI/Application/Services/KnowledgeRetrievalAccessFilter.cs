// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Domain.Repositories;
using XiHan.Framework.AI.Abstractions.Rag;
using XiHan.Framework.AI.Abstractions.Rag.Models;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.AI.Application.Services;

/// <summary>
/// Filters retrieved knowledge chunks by the caller's document visibility. Shared by the RAG query
/// service, the chat-assistant augmentation path and the knowledge-retrieval skill/MCP tool so all
/// retrieval surfaces enforce the same application-layer ACL as list/detail endpoints.
/// </summary>
public sealed class KnowledgeRetrievalAccessFilter(
    IKnowledgeDocumentRepository documents,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<RetrievedChunk>> FilterAsync(
        IEnumerable<RetrievedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var visible = new List<RetrievedChunk>();
        foreach (var chunk in chunks)
        {
            if (!long.TryParse(chunk.DocumentId, out var documentId))
                continue;
            var document = await documents.GetByIdAsync(documentId, cancellationToken);
            if (document is not null && KnowledgeAccessPolicy.CanView(document, currentUser.IsInRole))
                visible.Add(chunk);
        }
        return visible;
    }
}
