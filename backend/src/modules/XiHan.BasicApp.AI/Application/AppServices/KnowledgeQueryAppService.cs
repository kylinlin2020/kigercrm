// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using XiHan.BasicApp.AI.Application.Contracts;
using XiHan.BasicApp.AI.Application.Dtos;
using XiHan.BasicApp.AI.Application.Mappers;
using XiHan.BasicApp.AI.Domain.Permissions;
using XiHan.BasicApp.AI.Domain.Policies;
using XiHan.BasicApp.AI.Domain.Repositories;
using XiHan.BasicApp.AI.Infrastructure.Configuration;
using XiHan.Framework.AI.Abstractions.Chat;
using XiHan.Framework.AI.Abstractions.Rag;
using XiHan.Framework.AI.Abstractions.Rag.Models;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.MultiTenancy.Abstractions;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.AI.Application.AppServices;

/// <summary>
/// 知识检索/问答应用服务（RAG 试玩：检索 → 注入 → 会话生成带引用答案）
/// </summary>
[DynamicApi(Group = "BasicApp.AI", GroupName = "AI 服务", Tag = "知识检索")]
public sealed class KnowledgeQueryAppService : AiApplicationService, IKnowledgeQueryAppService
{
    private const int MaxTopK = 20;

    private readonly IKnowledgeRetriever _retriever;
    private readonly IRagPromptAugmenter _augmenter;
    private readonly IXiHanAiService _aiService;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly XiHanRagOptions _ragOptions;

    /// <summary>
    /// 构造函数
    /// </summary>
    public KnowledgeQueryAppService(
        IKnowledgeRetriever retriever,
        IRagPromptAugmenter augmenter,
        IXiHanAiService aiService,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IKnowledgeDocumentRepository documents,
        IOptions<XiHanRagOptions> ragOptions)
    {
        _retriever = retriever;
        _augmenter = augmenter;
        _aiService = aiService;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _documents = documents;
        _ragOptions = ragOptions.Value;
    }

    /// <summary>
    /// 检索知识并（可选）生成带引用的答案
    /// </summary>
    [PermissionAuthorize(KnowledgePermissionCodes.Execute)]
    [HttpPost]
    public async Task<KnowledgeQueryResultDto> QueryAsync(KnowledgeQueryDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Query);
        cancellationToken.ThrowIfCancellationRequested();

        var topK = Math.Clamp(input.TopK ?? _ragOptions.DefaultTopK, 1, MaxTopK);

        // 按当前租户过滤（无租户上下文=平台全局 TenantId=0）
        var filter = new RetrievalFilter { TenantId = _currentTenant.Id ?? 0 };
        var retrieved = await _retriever.RetrieveAsync(input.Query.Trim(), topK, filter, input.Provider, cancellationToken);

        // ACL: drop chunks whose document the caller may not view, before citations and prompt.
        var visible = new List<RetrievedChunk>(retrieved.Count);
        foreach (var chunk in retrieved)
        {
            if (!long.TryParse(chunk.DocumentId, out var documentId))
                continue;
            var document = await _documents.GetByIdAsync(documentId, cancellationToken);
            if (document is not null && KnowledgeAccessPolicy.CanView(document, _currentUser.IsInRole))
                visible.Add(chunk);
        }

        var result = new KnowledgeQueryResultDto
        {
            Citations = visible.Select(KnowledgeApplicationMapper.ToCitationDto).ToList()
        };

        // 检索到片段且开启问答时，注入 prompt 走会话生成答案
        if (input.Answer && visible.Count > 0)
        {
            var augmentedPrompt = _augmenter.Augment(input.Query.Trim(), visible);
            var response = await _aiService.ChatAsync(
                [new ChatMessage(ChatRole.User, augmentedPrompt)],
                new XiHanChatOptions { Provider = input.Provider },
                cancellationToken);
            result.Answer = response.Text;
        }

        return result;
    }
}
