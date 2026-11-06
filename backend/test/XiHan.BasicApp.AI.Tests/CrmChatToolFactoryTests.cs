// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.AI;
using XiHan.BasicApp.AI.Application.Tools;

namespace XiHan.BasicApp.AI.Tests;

/// <summary>
/// CRM Chat 工具输入边界测试。无效参数必须在触达请求级 CRM 服务前被拒绝。
/// </summary>
public sealed class CrmChatToolFactoryTests
{
    [Fact]
    public async Task SearchCustomers_ShouldRejectBlankKeywordBeforeResolvingCrmServices()
    {
        var factory = new CrmChatToolFactory(null!, null!);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(factory.CreateTools(CancellationToken.None)));

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "   " }));

        Assert.Equal("keyword", exception.ParamName);
    }

    [Fact]
    public async Task SearchCustomers_ShouldRejectKeywordLongerThan200Characters()
    {
        var factory = new CrmChatToolFactory(null!, null!);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(factory.CreateTools(CancellationToken.None)));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = new string('x', 201) }));

        Assert.Equal("keyword", exception.ParamName);
    }

    [Fact]
    public async Task SearchCustomers_ShouldHonorCancellationBeforeCallingCrmServices()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new CrmChatToolFactory(null!, null!);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(factory.CreateTools(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "Acme" }));
    }
}
