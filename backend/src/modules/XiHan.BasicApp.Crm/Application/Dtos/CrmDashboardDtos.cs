namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmDashboardBucketDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
    public int SharePercent { get; set; }
}

public sealed class CrmDashboardReportDto
{
    public IReadOnlyList<CrmDashboardBucketDto> Buckets { get; set; } = [];
    public int Total { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class CrmDashboardOverviewDto
{
    public int LeadsTotal { get; set; }
    public int OpportunitiesTotal { get; set; }
    public decimal OpportunityAmount { get; set; }
    public int ProspectsTotal { get; set; }
    public int RunsTotal { get; set; }
    public int ActiveRuns { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
}
