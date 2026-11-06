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

/// <summary>CRM Chat 工具输入边界、查询调用和脱敏顺序测试。</summary>
public sealed class CrmChatToolFactoryTests
{
    [Fact]
    public async Task SearchCustomers_ShouldRejectBlankKeywordBeforeResolvingCrmServices()
    {
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var function = GetFunction(factory, "crm_search_customers");

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "   " }));

        Assert.Equal("keyword", exception.ParamName);
    }

    [Fact]
    public async Task SearchCustomers_ShouldRejectKeywordLongerThan200Characters()
    {
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var function = GetFunction(factory, "crm_search_customers");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = new string('x', 201) }));

        Assert.Equal("keyword", exception.ParamName);
    }

    [Fact]
    public async Task SearchCustomers_ShouldHonorCancellationBeforeCallingCrmServices()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var function = GetFunction(factory, "crm_search_customers");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "Acme" }));
    }

    [Fact]
    public async Task SearchCustomers_ShouldUseQueryServiceAndMaskResultsBeforeReturningThem()
    {
        var query = new RecordingCustomerQuery();
        var fieldSecurity = new RecordingFieldSecurity();
        var permissionGuard = new RecordingPermissionGuard();
        var factory = new CrmChatToolFactory(query, null!, null!, fieldSecurity, permissionGuard);
        var function = GetFunction(factory, "crm_search_customers");

        _ = await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "  Acme  " });

        Assert.Equal("Acme", query.LastInput?.Keyword);
        Assert.Equal([XiHan.BasicApp.Crm.Domain.Permissions.CrmPermissionCodes.Read], permissionGuard.GrantedPermissions);
        Assert.True(fieldSecurity.MaskCalled);
        Assert.NotNull(fieldSecurity.MaskedResponse);
    }

    [Fact]
    public async Task SearchLeads_ShouldRejectBlankKeywordBeforeResolvingCrmServices()
    {
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var function = GetFunction(factory, "crm_search_leads");

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = " " }));

        Assert.Equal("keyword", exception.ParamName);
    }

    [Fact]
    public async Task SearchLeads_ShouldRejectKeywordLongerThan200Characters()
    {
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var function = GetFunction(factory, "crm_search_leads");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = new string('x', 201) }));

        Assert.Equal("keyword", exception.ParamName);
    }

    [Fact]
    public async Task SearchLeads_ShouldHonorCancellationBeforeCallingCrmServices()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var function = GetFunction(factory, "crm_search_leads");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "Acme" }));
    }

    [Fact]
    public async Task SearchLeads_ShouldUseQueryServiceAndMaskResultsBeforeReturningThem()
    {
        var query = new RecordingLeadQuery();
        var fieldSecurity = new RecordingFieldSecurity();
        var permissionGuard = new RecordingPermissionGuard();
        var factory = new CrmChatToolFactory(null!, query, null!, fieldSecurity, permissionGuard);
        var function = GetFunction(factory, "crm_search_leads");

        _ = await function.InvokeAsync(new Dictionary<string, object?> { ["keyword"] = "  Acme  " });

        Assert.Equal("Acme", query.LastInput?.Keyword);
        Assert.Equal([XiHan.BasicApp.Crm.Domain.Permissions.CrmPermissionCodes.Read], permissionGuard.GrantedPermissions);
        Assert.True(fieldSecurity.MaskCalled);
        Assert.NotNull(fieldSecurity.MaskedResponse);
    }

    [Fact]
    public async Task AllCrmReadTools_ShouldEnforceReadPermissionBeforeResolvingRecords()
    {
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new DenyingPermissionGuard());
        var calls = new (string Name, Dictionary<string, object?> Arguments)[]
        {
            ("crm_search_customers", new() { ["keyword"] = "Acme" }),
            ("crm_search_leads", new() { ["keyword"] = "Acme" }),
            ("crm_prospecting_list_campaigns", new()),
            ("crm_prospecting_list_strategies", new() { ["campaignId"] = 1L }),
            ("crm_prospecting_list_runs", new() { ["campaignId"] = 1L }),
            ("crm_prospecting_list_candidates", new() { ["runId"] = 1L }),
            ("crm_prospecting_get_candidate_review", new() { ["candidateId"] = 1L })
        };

        var guard = Assert.IsType<DenyingPermissionGuard>(
            typeof(CrmChatToolFactory).GetField("_permissionGuard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(factory));

        foreach (var call in calls)
        {
            var function = GetFunction(factory, call.Name);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await function.InvokeAsync(call.Arguments));
            Assert.Equal(XiHan.BasicApp.Crm.Domain.Permissions.CrmPermissionCodes.Read, guard.LastPermissionCode);
        }

        Assert.Equal(calls.Length, factory.CreateTools(CancellationToken.None).Count);
    }

    [Fact]
    public async Task ProspectingTools_ShouldRejectInvalidIdsBeforeResolvingProspectingService()
    {
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var strategies = GetFunction(factory, "crm_prospecting_list_strategies");
        var runs = GetFunction(factory, "crm_prospecting_list_runs");
        var candidates = GetFunction(factory, "crm_prospecting_list_candidates");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await strategies.InvokeAsync(new Dictionary<string, object?> { ["campaignId"] = 0L }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await runs.InvokeAsync(new Dictionary<string, object?> { ["campaignId"] = 0L }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await candidates.InvokeAsync(new Dictionary<string, object?> { ["runId"] = -1L }));
    }

    [Fact]
    public async Task ProspectingReview_ShouldRejectInvalidCandidateIdBeforeResolvingProspectingService()
    {
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var review = GetFunction(factory, "crm_prospecting_get_candidate_review");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await review.InvokeAsync(new Dictionary<string, object?> { ["candidateId"] = 0L }));
    }

    [Fact]
    public async Task ProspectingReview_ShouldHonorCancellationBeforeCallingServices()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var review = Assert.IsAssignableFrom<AIFunction>(
            Assert.Single(factory.CreateTools(cancellation.Token), tool =>
                string.Equals(tool.GetType().GetProperty("Name")?.GetValue(tool) as string, "crm_prospecting_get_candidate_review", StringComparison.Ordinal)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await review.InvokeAsync(new Dictionary<string, object?> { ["candidateId"] = 1L }));
    }

    [Fact]
    public async Task ProspectingTools_ShouldHonorCancellationBeforeCallingServices()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new CrmChatToolFactory(null!, null!, null!, null!, new RecordingPermissionGuard());
        var campaigns = Assert.IsAssignableFrom<AIFunction>(
            Assert.Single(factory.CreateTools(cancellation.Token), tool =>
                string.Equals(tool.GetType().GetProperty("Name")?.GetValue(tool) as string, "crm_prospecting_list_campaigns", StringComparison.Ordinal)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await campaigns.InvokeAsync(new Dictionary<string, object?>()));
    }

    private static AIFunction GetFunction(CrmChatToolFactory factory, string name) =>
        Assert.IsAssignableFrom<AIFunction>(
            Assert.Single(factory.CreateTools(CancellationToken.None), tool =>
                string.Equals(tool.GetType().GetProperty("Name")?.GetValue(tool) as string, name, StringComparison.Ordinal)));

    private sealed class RecordingCustomerQuery : ICrmCustomerQueryService
    {
        public CrmCustomerPageQueryDto? LastInput { get; private set; }

        public Task<PageResultDtoBase<CrmCustomerListItemDto>> GetPageAsync(
            CrmCustomerPageQueryDto input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(new PageResultDtoBase<CrmCustomerListItemDto>([], null!));
        }

        public Task<CrmCustomerDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLeadQuery : ICrmLeadQueryService
    {
        public CrmLeadPageQueryDto? LastInput { get; private set; }

        public Task<PageResultDtoBase<CrmLeadListItemDto>> GetPageAsync(
            CrmLeadPageQueryDto input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(new PageResultDtoBase<CrmLeadListItemDto>([], null!));
        }

        public Task<CrmLeadDetailDto?> GetDetailAsync(long id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class DenyingPermissionGuard : IOperationPermissionGuard
    {
        public string? LastPermissionCode { get; private set; }

        public Task EnsureGrantedAsync(string permissionCode, CancellationToken cancellationToken = default)
        {
            LastPermissionCode = permissionCode;
            throw new UnauthorizedAccessException($"Permission denied: {permissionCode}");
        }
    }

    private sealed class RecordingPermissionGuard : IOperationPermissionGuard
    {
        public List<string> GrantedPermissions { get; } = [];

        public Task EnsureGrantedAsync(string permissionCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GrantedPermissions.Add(permissionCode);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFieldSecurity : IFieldSecurityService
    {
        public bool MaskCalled { get; private set; }
        public object? MaskedResponse { get; private set; }

        public Task<IReadOnlyDictionary<string, EffectiveFieldRule>> ResolveAsync(
            Type entityType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task GuardQueryAsync(
            QueryConditions conditions, Type entityType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MaskAsync(object? response, CancellationToken cancellationToken = default)
        {
            MaskCalled = true;
            MaskedResponse = response;
            return Task.CompletedTask;
        }

        public Task EnsureCreatableAsync<TInput>(
            Type entityType, TInput input, CancellationToken cancellationToken = default)
            where TInput : class, new() => throw new NotSupportedException();

        public Task EnsureUpdatableAsync<TInput>(
            Type entityType, long id, TInput input, CancellationToken cancellationToken = default)
            where TInput : class => throw new NotSupportedException();
    }
}
