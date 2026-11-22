namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmCustomerPoolEntryDto
{
    public long BasicId { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int Status { get; set; }
    public long? ClaimedUserId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public int ClaimCount { get; set; }
    public DateTimeOffset? LastReleasedAtUtc { get; set; }
}

public sealed class CrmCustomerPoolAddDto
{
    public long CustomerId { get; set; }
    public string? Note { get; set; }
}

public sealed class CrmCustomerPoolActionDto
{
    public long EntryId { get; set; }
}
