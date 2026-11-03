using XiHan.BasicApp.Crm.Domain.Permissions;
using XiHan.BasicApp.Saas.Application.Pages;
using XiHan.BasicApp.Saas.Domain.Entities;

namespace XiHan.BasicApp.Crm.Application.Pages;

/// <summary>Single source of truth for CRM navigation pages and button permissions.</summary>
public static class PageRegistry
{
    public const string CrmDirectoryCode = "crm";

    public static IReadOnlyList<PageDescriptor> All { get; } =
    [
        new(CrmDirectoryCode, "CRM", "menu.crm", MenuType.Directory, "/crm", "Crm", null, null, null, "lucide:users", 500),
        new("crm_customers", "Customers", "menu.crm_customers", MenuType.Menu, "/crm/customers", "CrmCustomer", "Crm/Customer/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:building-2", 510),
        new("crm_contacts", "Contacts", "menu.crm_contacts", MenuType.Menu, "/crm/contacts", "CrmContact", "Crm/Contact/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:contact", 515),
        new("crm_leads", "Leads", "menu.crm_leads", MenuType.Menu, "/crm/leads", "CrmLead", "Crm/Lead/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:user-round-search", 520),
        new("crm_opportunities", "Opportunities", "menu.crm_opportunities", MenuType.Menu, "/crm/opportunities", "CrmOpportunity", "Crm/Opportunity/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:chart-no-axes-combined", 530),
        new("crm_tasks", "Tasks", "menu.crm_tasks", MenuType.Menu, "/crm/tasks", "CrmTask", "Crm/Task/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:list-checks", 540),
    ];

    public static IReadOnlyList<ButtonDescriptor> Buttons { get; } =
    [
        new("crm_customers.create", "Create", "crm_customers", CrmPermissionCodes.Create, 1),
        new("crm_customers.update", "Edit", "crm_customers", CrmPermissionCodes.Update, 2),
        new("crm_customers.delete", "Delete", "crm_customers", CrmPermissionCodes.Delete, 3),
        new("crm_contacts.create", "Create", "crm_contacts", CrmPermissionCodes.Create, 1),
        new("crm_contacts.update", "Edit", "crm_contacts", CrmPermissionCodes.Update, 2),
        new("crm_contacts.delete", "Delete", "crm_contacts", CrmPermissionCodes.Delete, 3),
        new("crm_leads.create", "Create", "crm_leads", CrmPermissionCodes.Create, 1),
        new("crm_leads.update", "Edit", "crm_leads", CrmPermissionCodes.Update, 2),
        new("crm_leads.delete", "Delete", "crm_leads", CrmPermissionCodes.Delete, 3),
        new("crm_leads.convert", "Convert", "crm_leads", CrmPermissionCodes.Execute, 4),
        new("crm_opportunities.create", "Create", "crm_opportunities", CrmPermissionCodes.Create, 1),
        new("crm_opportunities.update", "Edit", "crm_opportunities", CrmPermissionCodes.Update, 2),
        new("crm_opportunities.delete", "Delete", "crm_opportunities", CrmPermissionCodes.Delete, 3),
        new("crm_tasks.create", "Create", "crm_tasks", CrmPermissionCodes.Create, 1),
        new("crm_tasks.update", "Edit", "crm_tasks", CrmPermissionCodes.Update, 2),
        new("crm_tasks.delete", "Delete", "crm_tasks", CrmPermissionCodes.Delete, 3),
    ];
}
