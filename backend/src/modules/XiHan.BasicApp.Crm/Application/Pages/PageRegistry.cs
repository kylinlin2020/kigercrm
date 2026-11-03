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
        new("crm_activities", "Activities", "menu.crm_activities", MenuType.Menu, "/crm/activities", "CrmActivity", "Crm/Activity/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:history", 525),
        new("crm_leads", "Leads", "menu.crm_leads", MenuType.Menu, "/crm/leads", "CrmLead", "Crm/Lead/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:user-round-search", 520),
        new("crm_opportunities", "Opportunities", "menu.crm_opportunities", MenuType.Menu, "/crm/opportunities", "CrmOpportunity", "Crm/Opportunity/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:chart-no-axes-combined", 530),
        new("crm_tasks", "Tasks", "menu.crm_tasks", MenuType.Menu, "/crm/tasks", "CrmTask", "Crm/Task/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:list-checks", 540),
        new("crm_quotes", "Quotes", "menu.crm_quotes", MenuType.Menu, "/crm/quotes", "CrmQuote", "Crm/Quote/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:file-text", 550),
        new("crm_sales_orders", "Sales Orders", "menu.crm_sales_orders", MenuType.Menu, "/crm/sales-orders", "CrmSalesOrder", "Crm/SalesOrder/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:shopping-cart", 560),
        new("crm_shipments", "Shipments", "menu.crm_shipments", MenuType.Menu, "/crm/shipments", "CrmShipment", "Crm/Shipment/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:truck", 570),
    ];

    public static IReadOnlyList<ButtonDescriptor> Buttons { get; } =
    [
        new("crm_customers.create", "Create", "crm_customers", CrmPermissionCodes.Create, 1),
        new("crm_customers.update", "Edit", "crm_customers", CrmPermissionCodes.Update, 2),
        new("crm_customers.delete", "Delete", "crm_customers", CrmPermissionCodes.Delete, 3),
        new("crm_contacts.create", "Create", "crm_contacts", CrmPermissionCodes.Create, 1),
        new("crm_contacts.update", "Edit", "crm_contacts", CrmPermissionCodes.Update, 2),
        new("crm_contacts.delete", "Delete", "crm_contacts", CrmPermissionCodes.Delete, 3),
        new("crm_activities.create", "Create", "crm_activities", CrmPermissionCodes.Create, 1),
        new("crm_activities.update", "Edit", "crm_activities", CrmPermissionCodes.Update, 2),
        new("crm_activities.delete", "Delete", "crm_activities", CrmPermissionCodes.Delete, 3),
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
        new("crm_quotes.create", "Create", "crm_quotes", CrmPermissionCodes.Create, 1),
        new("crm_quotes.execute", "Execute", "crm_quotes", CrmPermissionCodes.Execute, 2),
        new("crm_sales_orders.create", "Create", "crm_sales_orders", CrmPermissionCodes.Create, 1),
        new("crm_sales_orders.update", "Edit", "crm_sales_orders", CrmPermissionCodes.Update, 2),
        new("crm_sales_orders.execute", "Execute", "crm_sales_orders", CrmPermissionCodes.Execute, 2),
        new("crm_shipments.create", "Create", "crm_shipments", CrmPermissionCodes.Create, 1),
        new("crm_shipments.execute", "Execute", "crm_shipments", CrmPermissionCodes.Execute, 2),
    ];
}
