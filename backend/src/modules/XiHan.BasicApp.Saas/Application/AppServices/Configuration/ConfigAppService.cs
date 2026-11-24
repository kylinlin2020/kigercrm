// Copyright (c) 2021-Present XiHanFun and contributors.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using XiHan.BasicApp.Saas.Application.Caching;
using XiHan.BasicApp.Saas.Application.Contracts;
using XiHan.BasicApp.Saas.Application.Dtos;
using XiHan.BasicApp.Saas.Application.Mappers;
using XiHan.BasicApp.Saas.Domain.DomainServices;
using XiHan.BasicApp.Saas.Domain.Entities;
using XiHan.BasicApp.Saas.Domain.Permissions;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Authorization.AspNetCore;
using XiHan.Framework.Uow.Attributes;
using XiHan.BasicApp.Saas.Application.Services;

namespace XiHan.BasicApp.Saas.Application.AppServices;

/// <summary>
/// 系统配置命令应用服务
/// </summary>
[Authorize]
[DynamicApi(Group = "BasicApp.Saas", GroupName = "系统SaaS服务", Tag = "系统配置")]
public sealed class ConfigAppService
    : SaasApplicationService, IConfigAppService
{
    private readonly IConfigDomainService _configDomainService;

    private readonly ISaasCacheInvalidator _cacheInvalidator;

    private readonly IFieldSecurityService _fieldSecurity;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConfigAppService(IConfigDomainService configDomainService, ISaasCacheInvalidator cacheInvalidator, IFieldSecurityService fieldSecurity)
    {
        _configDomainService = configDomainService;
        _cacheInvalidator = cacheInvalidator;
        _fieldSecurity = fieldSecurity;
    }

    /// <summary>
    /// 创建系统配置
    /// </summary>
    [UnitOfWork(true)]
    [PermissionAuthorize(SaasPermissionCodes.Config.Create)]
    public async Task<ConfigDetailDto> CreateConfigAsync(ConfigCreateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        // 字段安全：只读字段不能填写
        await _fieldSecurity.EnsureCreatableAsync(typeof(SysConfig), input, cancellationToken);

        var result = await _configDomainService.CreateConfigAsync(ConfigApplicationMapper.ToCreateCommand(input), cancellationToken);

        // 配置变更影响运行时配置值缓存，统一全失效（修复此前 InvalidateConfiguration 无调用点的失效空转）
        await _cacheInvalidator.InvalidateConfigurationAsync(cancellationToken: cancellationToken);

        return ConfigApplicationMapper.ToDetailDto(result.Config);
    }

    /// <summary>
    /// 删除系统配置
    /// </summary>
    [UnitOfWork(true)]
    [PermissionAuthorize(SaasPermissionCodes.Config.Delete)]
    public async Task DeleteConfigAsync(long id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _configDomainService.DeleteConfigAsync(id, cancellationToken);

        // 配置删除影响运行时配置值缓存，统一全失效
        await _cacheInvalidator.InvalidateConfigurationAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 原子清除租户配置值并写入启用的空值墓碑，避免平台兜底凭证静默复活。
    /// 删除与创建包裹在同一事务：任一步失败整笔回滚，不会出现"删了但没写墓碑"的中间态。
    /// </summary>
    [UnitOfWork(true)]
    [PermissionAuthorize(SaasPermissionCodes.Config.Delete)]
    public async Task ClearTenantConfigAsync(
        long existingId,
        ConfigCreateDto tombstone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tombstone);
        cancellationToken.ThrowIfCancellationRequested();
        await _fieldSecurity.EnsureCreatableAsync(typeof(SysConfig), tombstone, cancellationToken);
        await _configDomainService.DeleteConfigAsync(existingId, cancellationToken);
        await _configDomainService.CreateConfigAsync(ConfigApplicationMapper.ToCreateCommand(tombstone), cancellationToken);
        await _cacheInvalidator.InvalidateConfigurationAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 更新系统配置
    /// </summary>
    [UnitOfWork(true)]
    [PermissionAuthorize(SaasPermissionCodes.Config.Update)]
    public async Task<ConfigDetailDto> UpdateConfigAsync(ConfigUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        // 字段安全：只读字段不能改，表单交回的脱敏值还原为原值
        await _fieldSecurity.EnsureUpdatableAsync(typeof(SysConfig), input.BasicId, input, cancellationToken);

        var result = await _configDomainService.UpdateConfigAsync(ConfigApplicationMapper.ToUpdateCommand(input), cancellationToken);

        // 配置变更影响运行时配置值缓存，统一全失效
        await _cacheInvalidator.InvalidateConfigurationAsync(cancellationToken: cancellationToken);

        return ConfigApplicationMapper.ToDetailDto(result.Config);
    }

    /// <summary>
    /// 更新系统配置状态
    /// </summary>
    [UnitOfWork(true)]
    [PermissionAuthorize(SaasPermissionCodes.Config.Status)]
    public async Task<ConfigDetailDto> UpdateConfigStatusAsync(ConfigStatusUpdateDto input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        // 字段安全：只读字段不能改，表单交回的脱敏值还原为原值
        await _fieldSecurity.EnsureUpdatableAsync(typeof(SysConfig), input.BasicId, input, cancellationToken);

        var result = await _configDomainService.UpdateConfigStatusAsync(ConfigApplicationMapper.ToStatusCommand(input), cancellationToken);

        // 配置启停影响运行时配置值缓存，统一全失效
        await _cacheInvalidator.InvalidateConfigurationAsync(cancellationToken: cancellationToken);

        return ConfigApplicationMapper.ToDetailDto(result.Config);
    }
}
