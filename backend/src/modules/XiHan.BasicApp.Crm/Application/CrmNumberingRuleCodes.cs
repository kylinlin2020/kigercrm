namespace XiHan.BasicApp.Crm.Application;

/// <summary>
/// Stable rule codes used by CRM document creation. Administrators must provision matching
/// tenant or globally available numbering rules before clients opt in to server-side numbering.
/// </summary>
public static class CrmNumberingRuleCodes
{
    public const string Quote = "CRM_QUOTE";
    public const string SalesOrder = "CRM_SALES_ORDER";
    public const string Shipment = "CRM_SHIPMENT";
}
