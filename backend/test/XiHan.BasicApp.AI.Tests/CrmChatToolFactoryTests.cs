// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.AI;
using XiHan.BasicApp.AI.Application.Tools;
using XiHan.BasicApp.Crm.Application.Contracts;
using XiHan.BasicApp.Crm.Application.Dtos;
using XiHan.BasicApp.Saas.Application.Services;
using XiHan.Framework.Domain.Shared.Paging.Dtos;
using XiHan.Framework.Domain.Shared.Paging.Models;

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
    [Fact]
    public async Task SearchCustomers_ShouldUseQueryServiceAndMaskResultsBeforeReturningThem()
    {
        var query = new RecordingCustomerQuery();
        var fieldSecurity = new RecordingFieldSecurity();
        var factory = new CrmChatToolFactory(query, fieldSecurity);
        var function = Assert.IsAssignableFrom<AIFunction>(Assert.Single(factory.CreateTools(CancellationToken.None)));

        _ = await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "  Acme  " });

        Assert.Equal("Acme", query.LastInput?.Keyword);
        Assert.True(fieldSecurity.MaskCalled);
        Assert.NotNull(fieldSecurity.MaskedResponse);
    }

    private sealed class RecordingCustomerQuery : ICrmCustomerQueryService
    {
        public CrmCustomerPageQueryDto? LastInput { get; private set; }

        public Task<PageResultDtoBase<CrmCustomerListItemDto>> GetPageAsync(
            CrmCustomerPageQueryDto input,
            CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(new PageResultDtoBase<CrmCustomerListItemDto>([], null!));
        }

        public Task<CrmCustomerDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingFieldSecurity : IFieldSecurityService
    {
        public bool MaskCalled { get; private set; }
        public object? MaskedResponse { get; private set; }

        public Task<IReadOnlyDictionary<string, EffectiveFieldRule>> ResolveAsync(
            Type entityType,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task GuardQueryAsync(
            QueryConditions conditions,
            Type entityType,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MaskAsync(object? response, CancellationToken cancellationToken = default)
        {
            MaskCalled = true;
            MaskedResponse = response;
            return Task.CompletedTask;
        }

        public Task EnsureCreatableAsync<TInput>(
            Type entityType,
            TInput input,
            CancellationToken cancellationToken = default)
            where TInput : class, new() =>
            throw new NotSupportedException();

        public Task EnsureUpdatableAsync<TInput>(
            Type entityType,
            long id,
            TInput input,
            CancellationToken cancellationToken = default)
            where TInput : class =>
            throw new NotSupportedException();
    }

}
