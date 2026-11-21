using XiHan.BasicApp.AI.Application.Contracts;
using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.BasicApp.AI.Domain.Enums;
using XiHan.BasicApp.AI.Domain.Permissions;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.AI.Application.AppServices;

/// <summary>
/// Knowledge document ACL management. Restricted documents with an empty role allowlist are
/// visible only to super-admins — fail-closed by default.
/// </summary>
[DynamicApi(Group = "BasicApp.AI", GroupName = "AI 服务", Tag = "KnowledgeAccess")]
public sealed class KnowledgeAccessAppService(
    IKnowledgeDocumentRepository documents)
    : AiApplicationService, IKnowledgeAccessAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(AiPermissionCodes.Update)]
    public async Task<KnowledgeAccessDto> UpdateAsync(
        KnowledgeAccessUpdateDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(typeof(KnowledgeVisibility), input.Visibility))
            throw new ArgumentOutOfRangeException(nameof(input.Visibility));
        var document = await documents.GetByIdAsync(input.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("Knowledge document does not exist.");
        document.Visibility = (KnowledgeVisibility)input.Visibility;
        document.AllowedRoles = document.Visibility == KnowledgeVisibility.Restricted
            ? KnowledgeAccessPolicy.NormalizeRoles(input.AllowedRoles)
            : null; // public documents carry no allowlist
        await documents.UpdateAsync(document, cancellationToken);
        return ToDto(document);
    }

    [PermissionAuthorize(AiPermissionCodes.Read)]
    public async Task<KnowledgeAccessDto> GetAsync(long documentId, CancellationToken cancellationToken = default)
    {
        var document = await documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException("Knowledge document does not exist.");
        return ToDto(document);
    }

    private static KnowledgeAccessDto ToDto(XiHan.BasicApp.AI.Domain.Entities.SysKnowledgeDocument document)
        => new()
        {
            DocumentId = document.BasicId,
            Visibility = (int)document.Visibility,
            AllowedRoles = KnowledgeAccessPolicy.ParseRoles(document.AllowedRoles).ToArray()
        };
}
