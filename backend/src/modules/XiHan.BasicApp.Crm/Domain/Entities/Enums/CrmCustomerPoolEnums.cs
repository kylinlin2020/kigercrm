namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

/// <summary>State of a customer in the shared pool.</summary>
public enum CrmCustomerPoolStatus
{
    Available = 0,
    Claimed = 1,
    Recovered = 2,
}
