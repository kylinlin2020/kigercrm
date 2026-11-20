namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmCustomerImportInputDto
{
    public string RowsJson { get; set; } = string.Empty;
    public Dictionary<string, string> Mapping { get; set; } = [];
    public string? FileName { get; set; }
}

public sealed class CrmCustomerImportPreviewRowDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Website { get; set; }
    public string? CountryRegion { get; set; }
    public string? Industry { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
    public string? DuplicateReason { get; set; }
}

public sealed class CrmCustomerImportPreviewDto
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Duplicates { get; set; }
    public IReadOnlyList<CrmCustomerImportPreviewRowDto> Rows { get; set; } = [];
}

public sealed class CrmCustomerImportResultDto
{
    public int Total { get; set; }
    public int Created { get; set; }
    public int Invalid { get; set; }
    public int Duplicates { get; set; }
    public IReadOnlyList<long> CreatedCustomerIds { get; set; } = [];
    public long ImportHistoryId { get; set; }
}