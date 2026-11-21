using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Enums;

namespace XiHan.BasicApp.AI.Domain.Policies;

/// <summary>
/// Incremental re-index decision. A document only needs re-embedding when its raw content changed
/// since the last successful index, or when it has never been indexed successfully.
/// </summary>
public static class KnowledgeIndexPolicy
{
    public static bool NeedsReindex(SysKnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Status != KnowledgeIndexStatus.Indexed || document.ChunkCount <= 0)
            return true; // Failed/pending documents must always be (re)indexed.
        if (string.IsNullOrWhiteSpace(document.ContentHash))
            return true; // Unknown content hash -> cannot prove unchanged.
        return !string.Equals(document.ContentHash, document.IndexedContentHash, StringComparison.OrdinalIgnoreCase);
    }

    public static void MarkIndexed(SysKnowledgeDocument document)
        => document.IndexedContentHash = document.ContentHash;

    public static void MarkFailed(SysKnowledgeDocument document)
        => document.IndexedContentHash = null;
}
