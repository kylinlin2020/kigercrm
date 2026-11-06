// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Microsoft.Extensions.AI;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.AI.Application.Tools;

/// <summary>
/// Creates request-scoped, read-only CRM functions for an authenticated assistant reply.
/// </summary>
/// <remarks>
/// This factory is scoped. Its delegates close over the current request's CRM application
/// service and field-security service; they must never be promoted to singleton Skill/MCP tools.
/// </remarks>
public sealed class CrmChatToolFactory
{
    private readonly ICrmCustomerQueryService _customerQuery;
    private readonly ICrmLeadQueryService _leadQuery;
    private readonly ICrmProspectingAppService _prospecting;
    private readonly IFieldSecurityService _fieldSecurity;

    public CrmChatToolFactory(
        ICrmCustomerQueryService customerQuery,
        ICrmLeadQueryService leadQuery,
        ICrmProspectingAppService prospecting,
        IFieldSecurityService fieldSecurity)
    {
        _customerQuery = customerQuery;
        _leadQuery = leadQuery;
        _prospecting = prospecting;
        _fieldSecurity = fieldSecurity;
    }

    /// <summary>
    /// Creates the explicitly allow-listed CRM tools for one authenticated chat request.
    /// </summary>
    public IList<AITool> CreateTools(CancellationToken cancellationToken)
    {
        return
        [
            AIFunctionFactory.Create(
                async ([Description("要搜索的客户名称、法定名称、网站关键词；不得填写租户或用户身份信息。")] string keyword) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(keyword))
                    {
                        throw new ArgumentException("客户搜索关键词不能为空。", nameof(keyword));
                    }

                    keyword = keyword.Trim();
                    if (keyword.Length > 200)
                    {
                        throw new ArgumentOutOfRangeException(nameof(keyword), "客户搜索关键词不能超过 200 个字符。");
                    }

                    var page = await _customerQuery.GetPageAsync(
                        new CrmCustomerPageQueryDto { Keyword = keyword },
                        cancellationToken);

                    // Internal Tool results do not pass through the HTTP response filter.
                    // Mask this request-owned DTO graph before it enters model context.
                    await _fieldSecurity.MaskAsync(page, cancellationToken);
                    return page.Items.ToList();
                },
                name: "crm_search_customers",
                description: "只读搜索当前登录用户有权访问的 CRM 客户。按客户名称、法定名称或网站关键词搜索；遵守当前用户的数据范围和字段脱敏规则。不得尝试读取其他租户或指定 Owner。"),
            AIFunctionFactory.Create(
                async ([Description("要搜索的线索公司名称、联系人或邮箱关键词；不得填写租户或用户身份信息。")] string keyword) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(keyword))
                    {
                        throw new ArgumentException("线索搜索关键词不能为空。", nameof(keyword));
                    }

                    keyword = keyword.Trim();
                    if (keyword.Length > 200)
                    {
                        throw new ArgumentOutOfRangeException(nameof(keyword), "线索搜索关键词不能超过 200 个字符。");
                    }

                    var page = await _leadQuery.GetPageAsync(
                        new CrmLeadPageQueryDto { Keyword = keyword },
                        cancellationToken);

                    // Lead results also bypass the HTTP response filter on this internal Tool path.
                    await _fieldSecurity.MaskAsync(page, cancellationToken);
                    return page.Items.ToList();
                },
                name: "crm_search_leads",
                description: "只读搜索当前登录用户有权访问的 CRM 线索。按公司名称、联系人或邮箱关键词搜索；遵守当前用户的数据范围和字段脱敏规则。不得尝试读取其他租户或指定 Owner。"),
            AIFunctionFactory.Create(
                async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var items = await _prospecting.GetCampaignsAsync(cancellationToken);
                    return items.Select(item => new
                    {
                        item.BasicId,
                        item.Name,
                        item.Objective,
                        item.ProductScope,
                        item.TargetMarkets,
                        item.IdealCustomerProfile,
                        item.Status,
                        item.ActiveStrategyVersion,
                        item.CreatedTime
                    }).ToArray();
                },
                name: "crm_prospecting_list_campaigns",
                description: "只读列出当前登录用户数据范围内的自动获客 Campaign。不得尝试访问其他用户或租户的数据。"),
            AIFunctionFactory.Create(
                async ([Description("CRM 自动获客 Campaign 的数字 ID。只能使用当前会话已获得的 ID。")] long campaignId) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (campaignId <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(campaignId));
                    }

                    var items = await _prospecting.GetCampaignRunsAsync(campaignId, cancellationToken);
                    return items.Select(item => new
                    {
                        item.BasicId,
                        item.CampaignId,
                        item.StrategyVersion,
                        item.Status,
                        item.CreatedTime,
                        item.StartedAt,
                        item.CompletedAt,
                        item.RawRecordCount,
                        item.CandidateCount,
                        item.FailureCount
                    }).ToArray();
                },
                name: "crm_prospecting_list_runs",
                description: "只读列出指定且当前用户有权访问的 Campaign 的自动获客运行记录。不得尝试访问其他用户或租户的数据。"),
            AIFunctionFactory.Create(
                async ([Description("CRM 自动获客 Run 的数字 ID。只能使用当前会话已获得的 ID。")] long runId) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (runId <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(runId));
                    }

                    var items = await _prospecting.GetRunCandidatesAsync(runId, cancellationToken: cancellationToken);
                    return items.Select(item => new
                    {
                        item.BasicId,
                        item.RunId,
                        item.Title,
                        item.CompanyName,
                        item.Website,
                        item.Status,
                        item.CreatedTime
                    }).ToArray();
                },
                name: "crm_prospecting_list_candidates",
                description: "只读列出指定且当前用户有权访问的自动获客 Run 的候选客户。出于数据最小化，不返回候选人的邮箱、联系人姓名、供应商原始记录 ID、转换记录 ID、摘要或重复判定细节。不得尝试访问其他用户或租户的数据。"),
            AIFunctionFactory.Create(
                async ([Description("CRM 自动获客候选客户的数字 ID。只能使用当前会话已获得的 ID。")] long candidateId) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (candidateId <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(candidateId));
                    }

                    var detail = await _prospecting.GetCandidateReviewDetailAsync(candidateId, cancellationToken);
                    // The review-detail wrapper has no single entity mapping; mask its mapped Candidate DTO explicitly.
                    await _fieldSecurity.MaskAsync(detail.Candidate, cancellationToken);
                    return new
                    {
                        Candidate = new
                        {
                            detail.Candidate.BasicId,
                            detail.Candidate.RunId,
                            detail.Candidate.Title,
                            detail.Candidate.CompanyName,
                            detail.Candidate.Website,
                            detail.Candidate.Status,
                            detail.Candidate.CreatedTime
                        },
                        Evidence = detail.Evidence.Select(item => new
                        {
                            item.FieldName,
                            item.EvidenceType,
                            item.VerificationStatus,
                            item.PublishedAtUtc,
                            item.RetrievedAtUtc
                        }).ToArray(),
                        ScoreHistory = detail.ScoreHistory.Select(item => new
                        {
                            item.ScorecardVersion,
                            item.Total,
                            item.Priority,
                            item.IdentityPoints,
                            item.ProcurementPoints,
                            item.ProductFitPoints,
                            item.MarketFitPoints,
                            item.RecencyPoints,
                            item.EvidencePoints,
                            item.ContactPoints,
                            item.EvaluatedAtUtc
                        }).ToArray()
                    };
                },
                name: "crm_prospecting_get_candidate_review",
                description: "只读获取当前用户有权访问的候选客户审核摘要和评分历史。仅返回脱敏后的字段名、证据类型/验证状态、时间和评分，不返回邮箱、联系人姓名、证据 URL、供应商记录 ID、原始证据摘要或哈希。不得执行审核或转线索。")
        ];
    }
}
