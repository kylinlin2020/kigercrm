using Microsoft.AspNetCore.Authorization;
using XiHan.Framework.Application.Attributes;
using XiHan.Framework.Application.Services;

namespace XiHan.BasicApp.Crm.Application;

/// <summary>Base class for authenticated CRM application services.</summary>
[Authorize]
[DynamicApi(Group = "BasicApp.Crm", GroupName = "CRM Services")]
public abstract class CrmApplicationService : ApplicationServiceBase
{
}
