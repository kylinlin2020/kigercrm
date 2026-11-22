namespace XiHan.BasicApp.Crm.Application.Dtos;

/// <summary>A locally rendered export file; the client decides whether to download it.</summary>
public sealed class CrmBackgroundResearchExportDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
