using Microsoft.AspNetCore.Authorization;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Application.Services;
using XiHan.Framework.Security.Users;

namespace XiHan.BasicApp.Crm.Application;

/// <summary>Base class for authenticated CRM application services.</summary>
[Authorize]
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services")]
public abstract class CrmApplicationService : ApplicationServiceBase
{
    /// <summary>Current authenticated user id, or null when the request is not authenticated.</summary>
    protected long? CurrentUserId
        => (ServiceProvider.GetService(typeof(ICurrentUser)) as ICurrentUser)?.UserId;
}
