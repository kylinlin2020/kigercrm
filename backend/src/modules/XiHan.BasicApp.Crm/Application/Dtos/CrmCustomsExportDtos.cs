namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmCustomsFilterDto
{
    public string? Industry { get; set; }
    public string? HsPrefix { get; set; }
    public string? CountryCode { get; set; }
}

public sealed class CrmCustomsInputDto
{
    public string RowsJson { get; set; } = string.Empty;
    public CrmCustomsFilterDto? Filter { get; set; }
}

public sealed class CrmCustomsRowDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string HsCode { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string ExportDate { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ProductDescription { get; set; }
}

public sealed class CrmCustomsValidationRowDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string HsCode { get; set; } = string.Empty;
    public IReadOnlyList<string> Errors { get; set; } = [];
}

public sealed class CrmCustomsValidationDto
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public IReadOnlyList<CrmCustomsValidationRowDto> Rows { get; set; } = [];
}

public sealed class CrmCustomsFilterResultDto
{
    public int Total { get; set; }
    public int Matched { get; set; }
    public IReadOnlyList<CrmCustomsRowDto> Rows { get; set; } = [];
}

public sealed class CrmCustomsExportResultDto
{
    public int Exported { get; set; }
    public string Csv { get; set; } = string.Empty;
}