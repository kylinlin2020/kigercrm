using XiHan.BasicApp.Saas.Application.Extensions;
using XiHan.BasicApp.Saas.Application.QueryServices;

namespace XiHan.BasicApp.Crm.Application.AppServices;

/// <summary>Prevents callers from assigning CRM records outside their resolved user data scope.</summary>
internal static class CrmAssignmentAuthorization
{
    public static async Task EnsureAssignableAsync(
        IUserDataScopeFilterService dataScope,
        long? targetUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataScope);
        cancellationToken.ThrowIfCancellationRequested();
        if (targetUserId is null) return;

        var scope = await dataScope.ResolveAccessibleUsersAsync(DateTimeOffset.UtcNow, cancellationToken);
        if (!scope.Unrestricted && !scope.UserIds.Contains(targetUserId.Value))
        {
            throw new UnauthorizedAccessException("The selected owner or assignee is outside the caller's allowed data scope.");
        }
    }
}
