namespace XiHan.BasicApp.Crm.Domain.Entities.Enums;

public enum CrmTradeDocumentType
{
    Invoice = 0,
    PackingList = 1,
    BillOfLading = 2,
}

public enum CrmTradeDocumentImportStatus
{
    Pending = 0,
    Confirmed = 1,
    Rejected = 2,
}
