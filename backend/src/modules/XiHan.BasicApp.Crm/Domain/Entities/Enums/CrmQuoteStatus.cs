namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>Lifecycle state of a customer quotation.</summary>
public enum CrmQuoteStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    Expired = 4,
    Converted = 5
}
