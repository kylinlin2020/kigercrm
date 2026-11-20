namespace XiHan.BasicApp.Crm.Application.Dtos;

public sealed class CrmBusinessCardImportInputDto
{
    public string RowsJson { get; set; } = string.Empty;
    public string? BatchId { get; set; }
}

public sealed class CrmBusinessCardPreviewRowDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? CompanyName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? JobTitle { get; set; }
    public string? Website { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? PhoneDigits { get; set; }
    public string? WebsiteHost { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
    public string? DuplicateReason { get; set; }
}

public sealed class CrmBusinessCardPreviewDto
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int Duplicates { get; set; }
    public IReadOnlyList<CrmBusinessCardPreviewRowDto> Rows { get; set; } = [];
}

public sealed class CrmBusinessCardImportResultDto
{
    public string BatchId { get; set; } = string.Empty;
    public int Staged { get; set; }
    public int Invalid { get; set; }
    public int DuplicateStaged { get; set; }
}

public sealed class CrmBusinessCardRowDto
{
    public long BasicId { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? CompanyName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? JobTitle { get; set; }
    public string? Website { get; set; }
    public int Status { get; set; }
    public string? DuplicateReason { get; set; }
    public string? RejectReason { get; set; }
    public long? ConvertedLeadId { get; set; }
}

public sealed class CrmBusinessCardDecisionDto
{
    public long RowId { get; set; }
    public string? Reason { get; set; }
}
