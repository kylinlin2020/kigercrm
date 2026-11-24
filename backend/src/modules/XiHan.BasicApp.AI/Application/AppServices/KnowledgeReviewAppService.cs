using XiHan.BasicApp.AI.Application;
using XiHan.BasicApp.AI.Application.Contracts;
using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Enums;
using XiHan.BasicApp.AI.Domain.Permissions;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Domain.Repositories;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Security.Users;
using XiHan.Framework.Uow.Attributes;

namespace XiHan.BasicApp.AI.Application.AppServices;

/// <summary>
/// Knowledge document review workflow. Review state is separate from indexing status: an indexed
/// document may still await publication approval, and editing raw content requires a new decision.
/// </summary>
[DynamicApi(Group = "BasicApp.AI", GroupName = "AI 服务", Tag = "KnowledgeReview")]
public sealed class KnowledgeReviewAppService(
    IKnowledgeDocumentRepository documents,
    IKnowledgeReviewRepository reviews,
    ICurrentUser currentUser)
    : AiApplicationService, IKnowledgeReviewAppService
{
    [UnitOfWork(true)]
    [PermissionAuthorize(KnowledgePermissionCodes.Execute)]
    public async Task<KnowledgeReviewDto> SubmitAsync(long documentId, CancellationToken cancellationToken = default)
    {
        if (documentId <= 0) throw new ArgumentOutOfRangeException(nameof(documentId));
        var document = await documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException("Knowledge document does not exist.");
        var latest = await reviews.GetLatestByDocumentAsync(documentId, cancellationToken);
        var lastApprovedHash = (await reviews.GetByDocumentAsync(documentId, cancellationToken))
            .FirstOrDefault(item => item.Status == KnowledgeReviewStatus.Approved)?.VersionHash;
        var currentHash = KnowledgeReviewPolicy.HashContent(document.RawContent);
        var decision = KnowledgeReviewPolicy.CanSubmit(latest?.Status, currentHash, lastApprovedHash);
        if (!decision.Allowed)
            throw new InvalidOperationException($"Knowledge review not required: {decision.Reason}.");
        var created = await reviews.AddAsync(new SysKnowledgeDocumentReview
        {
            DocumentId = documentId,
            Status = KnowledgeReviewStatus.Pending,
            VersionHash = currentHash
        }, cancellationToken);
        return ToDto(created);
    }

    [UnitOfWork(true)]
    [PermissionAuthorize(KnowledgePermissionCodes.Execute)]
    public async Task<KnowledgeReviewDto> DecideAsync(
        KnowledgeReviewDecisionDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var review = await reviews.GetByIdAsync(input.ReviewId, cancellationToken)
            ?? throw new InvalidOperationException("Knowledge review does not exist.");
        var decision = KnowledgeReviewPolicy.CanDecide(review.Status, input.Approve, input.Comment);
        if (!decision.Allowed)
            throw new InvalidOperationException($"Knowledge review decision rejected: {decision.Reason}.");
        review.Status = input.Approve ? KnowledgeReviewStatus.Approved : KnowledgeReviewStatus.Rejected;
        review.Comment = input.Comment?.Trim();
        review.ReviewerId = currentUser.UserId;
        review.ReviewedAtUtc = DateTimeOffset.UtcNow;
        return ToDto(await reviews.UpdateAsync(review, cancellationToken));
    }

    [PermissionAuthorize(KnowledgePermissionCodes.Read)]
    public async Task<IReadOnlyList<KnowledgeReviewDto>> GetReviewsAsync(long documentId, CancellationToken cancellationToken = default)
        => (await reviews.GetByDocumentAsync(documentId, cancellationToken)).Select(ToDto).ToArray();

    private static KnowledgeReviewDto ToDto(SysKnowledgeDocumentReview entity) => new()
    {
        BasicId = entity.BasicId,
        DocumentId = entity.DocumentId,
        Status = (int)entity.Status,
        VersionHash = entity.VersionHash,
        Comment = entity.Comment,
        ReviewerId = entity.ReviewerId,
        ReviewedAtUtc = entity.ReviewedAtUtc,
        CreatedTime = entity.CreatedTime
    };
}
