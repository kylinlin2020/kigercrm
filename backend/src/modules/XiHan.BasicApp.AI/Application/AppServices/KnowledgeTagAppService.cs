using System.Text.Json;
using XiHan.BasicApp.AI.Application.Contracts;
using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.BasicApp.AI.Domain.Permissions;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.AI.Application.AppServices;

/// <summary>
/// Knowledge tagging and duplicate detection. Tags are normalised deterministically; duplicates
/// are byte-identical raw-content groups, reported but never auto-merged or deleted.
/// </summary>
[DynamicApi(Group = "BasicApp.AI", GroupName = "AI 服务", Tag = "KnowledgeTags")]
public sealed class KnowledgeTagAppService(
    IKnowledgeDocumentRepository documents)
    : AiApplicationService, IKnowledgeTagAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [UnitOfWork(true)]
    [PermissionAuthorize(AiPermissionCodes.Update)]
    public async Task<KnowledgeTagItemDto[]> SetTagsAsync(
        KnowledgeTagsUpdateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var document = await documents.GetByIdAsync(input.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("Knowledge document does not exist.");
        var tags = KnowledgeTagPolicy.NormalizeTags(input.Tags);
        document.TagsJson = JsonSerializer.Serialize(tags, JsonOptions);
        await documents.UpdateAsync(document, cancellationToken);
        return await GetTagsAsync(cancellationToken);
    }

    [PermissionAuthorize(AiPermissionCodes.Read)]
    public async Task<KnowledgeTagItemDto[]> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await documents.GetAllAsync(cancellationToken);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var tag in KnowledgeTagPolicy.ParseTags(row.TagsJson))
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
        }
        return counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new KnowledgeTagItemDto { Tag = pair.Key, DocumentCount = pair.Value })
            .ToArray();
    }

    [PermissionAuthorize(AiPermissionCodes.Read)]
    public async Task<KnowledgeDuplicateReportDto> GetDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await documents.GetAllAsync(cancellationToken);
        var groups = KnowledgeTagPolicy.FindDuplicates(rows.Select(row =>
            (row.BasicId, row.Title, row.ContentHash)).ToArray());
        return new KnowledgeDuplicateReportDto
        {
            Groups = groups.Count,
            DuplicateDocuments = groups.Sum(group => group.DocumentIds.Count),
            Items = groups.Select(group => new KnowledgeDuplicateGroupDto
            {
                Hash = group.Hash,
                DocumentIds = group.DocumentIds.ToArray(),
                Titles = group.Titles.ToArray()
            }).ToArray()
        };
    }
}
