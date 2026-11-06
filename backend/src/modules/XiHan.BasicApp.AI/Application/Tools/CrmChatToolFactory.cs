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
    private readonly IFieldSecurityService _fieldSecurity;

    public CrmChatToolFactory(
        ICrmCustomerQueryService customerQuery,
        IFieldSecurityService fieldSecurity)
    {
        _customerQuery = customerQuery;
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
                description: "只读搜索当前登录用户有权访问的 CRM 客户。按客户名称、法定名称或网站关键词搜索；遵守当前用户的数据范围和字段脱敏规则。不得尝试读取其他租户或指定 Owner。")
        ];
    }
}
