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
        new("crm_customers", "客户", "menu.crm_customers", MenuType.Menu, "/crm/customers", "CrmCustomer", "Crm/Customer/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:building-2", 510),
        new("crm_contacts", "联系人", "menu.crm_contacts", MenuType.Menu, "/crm/contacts", "CrmContact", "Crm/Contact/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:contact", 515),
        new("crm_activities", "活动记录", "menu.crm_activities", MenuType.Menu, "/crm/activities", "CrmActivity", "Crm/Activity/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:history", 525),
        new("crm_leads", "销售线索", "menu.crm_leads", MenuType.Menu, "/crm/leads", "CrmLead", "Crm/Lead/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:user-round-search", 520),
        new("crm_opportunities", "商机", "menu.crm_opportunities", MenuType.Menu, "/crm/opportunities", "CrmOpportunity", "Crm/Opportunity/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:chart-no-axes-combined", 530),
        new("crm_tasks", "任务", "menu.crm_tasks", MenuType.Menu, "/crm/tasks", "CrmTask", "Crm/Task/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:list-checks", 540),
        new("crm_quotes", "报价", "menu.crm_quotes", MenuType.Menu, "/crm/quotes", "CrmQuote", "Crm/Quote/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:file-text", 550),
        new("crm_sales_orders", "销售订单", "menu.crm_sales_orders", MenuType.Menu, "/crm/sales-orders", "CrmSalesOrder", "Crm/SalesOrder/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:shopping-cart", 560),
        new("crm_shipments", "发货管理", "menu.crm_shipments", MenuType.Menu, "/crm/shipments", "CrmShipment", "Crm/Shipment/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:truck", 570),
        new("crm_prospecting", "自动获客", "menu.crm_prospecting", MenuType.Menu, "/crm/prospecting", "CrmProspecting", "Crm/Prospecting/Index", CrmDirectoryCode, CrmPermissionCodes.Read, "lucide:radar", 580),
    ];

    public static IReadOnlyList<ButtonDescriptor> Buttons { get; } =
    [
        new("crm_customers.create", "新建", "crm_customers", CrmPermissionCodes.Create, 1),
        new("crm_customers.update", "编辑", "crm_customers", CrmPermissionCodes.Update, 2),
        new("crm_customers.delete", "删除", "crm_customers", CrmPermissionCodes.Delete, 3),
        new("crm_customers.export", "导出", "crm_customers", CrmPermissionCodes.Execute, 4),
        new("crm_contacts.create", "新建", "crm_contacts", CrmPermissionCodes.Create, 1),
        new("crm_contacts.update", "编辑", "crm_contacts", CrmPermissionCodes.Update, 2),
        new("crm_contacts.delete", "删除", "crm_contacts", CrmPermissionCodes.Delete, 3),
        new("crm_contacts.export", "导出", "crm_contacts", CrmPermissionCodes.Execute, 4),
        new("crm_activities.create", "新建", "crm_activities", CrmPermissionCodes.Create, 1),
        new("crm_activities.update", "编辑", "crm_activities", CrmPermissionCodes.Update, 2),
        new("crm_activities.delete", "删除", "crm_activities", CrmPermissionCodes.Delete, 3),
        new("crm_leads.create", "新建", "crm_leads", CrmPermissionCodes.Create, 1),
        new("crm_leads.update", "编辑", "crm_leads", CrmPermissionCodes.Update, 2),
        new("crm_leads.delete", "删除", "crm_leads", CrmPermissionCodes.Delete, 3),
        new("crm_leads.convert", "转化", "crm_leads", CrmPermissionCodes.Execute, 4),
        new("crm_leads.export", "导出", "crm_leads", CrmPermissionCodes.Execute, 5),
        new("crm_opportunities.create", "新建", "crm_opportunities", CrmPermissionCodes.Create, 1),
        new("crm_opportunities.update", "编辑", "crm_opportunities", CrmPermissionCodes.Update, 2),
        new("crm_opportunities.delete", "删除", "crm_opportunities", CrmPermissionCodes.Delete, 3),
        new("crm_opportunities.export", "导出", "crm_opportunities", CrmPermissionCodes.Execute, 4),
        new("crm_tasks.create", "新建", "crm_tasks", CrmPermissionCodes.Create, 1),
        new("crm_tasks.update", "编辑", "crm_tasks", CrmPermissionCodes.Update, 2),
        new("crm_tasks.delete", "删除", "crm_tasks", CrmPermissionCodes.Delete, 3),
        new("crm_quotes.create", "新建", "crm_quotes", CrmPermissionCodes.Create, 1),
        new("crm_quotes.execute", "执行", "crm_quotes", CrmPermissionCodes.Execute, 2),
        new("crm_quotes.export", "导出", "crm_quotes", CrmPermissionCodes.Execute, 3),
        new("crm_sales_orders.create", "新建", "crm_sales_orders", CrmPermissionCodes.Create, 1),
        new("crm_sales_orders.update", "编辑", "crm_sales_orders", CrmPermissionCodes.Update, 2),
        new("crm_sales_orders.execute", "执行", "crm_sales_orders", CrmPermissionCodes.Execute, 2),
        new("crm_sales_orders.export", "导出", "crm_sales_orders", CrmPermissionCodes.Execute, 3),
        new("crm_shipments.create", "新建", "crm_shipments", CrmPermissionCodes.Create, 1),
        new("crm_shipments.execute", "执行", "crm_shipments", CrmPermissionCodes.Execute, 2),
        new("crm_shipments.export", "导出", "crm_shipments", CrmPermissionCodes.Execute, 3),
        new("crm_prospecting.create", "新建", "crm_prospecting", CrmPermissionCodes.Create, 1),
        new("crm_prospecting.execute", "执行", "crm_prospecting", CrmPermissionCodes.Execute, 2),
    ];
}
