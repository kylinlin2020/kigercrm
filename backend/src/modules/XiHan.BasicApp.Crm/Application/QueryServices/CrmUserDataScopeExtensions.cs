using System.Linq.Expressions;
using XiHan.BasicApp.Saas.Application.Extensions;
using XiHan.BasicApp.Saas.Application.QueryServices;
using XiHan.Framework.Domain.Shared.Paging.Models;

namespace XiHan.BasicApp.Crm.Application.QueryServices;

/// <summary>Applies BasicApp user data-scope decisions to CRM ownership queries.</summary>
internal static class CrmUserDataScopeExtensions
{
    public static async Task<UserDataScopeFilter> ApplyOwnerScopeAsync<TEntity>(
        this QueryConditions conditions,
        IUserDataScopeFilterService dataScope,
        Expression<Func<TEntity, long?>> ownerSelector,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(dataScope);
        var scope = await dataScope.ResolveAccessibleUsersAsync(now, cancellationToken);
        if (!scope.Unrestricted)
        {
            if (scope.UserIds.Count == 0)
            {
                // An empty IN set must never be interpreted as no filter by a provider.
                conditions.AddFilterIn<TEntity, long?>(ownerSelector, new object[] { long.MinValue });
            }
            else
            {
                conditions.AddFilterIn<TEntity, long?>(ownerSelector, scope.UserIds.Cast<object>());
            }
        }

        return scope;
    }

    public static bool CanReadOwnedRecord(this UserDataScopeFilter scope, long? ownerUserId) =>
        scope.Unrestricted || (ownerUserId.HasValue && scope.UserIds.Contains(ownerUserId.Value));
}
