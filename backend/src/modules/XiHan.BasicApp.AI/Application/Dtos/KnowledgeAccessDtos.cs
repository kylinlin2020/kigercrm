namespace XiHan.BasicApp.AI.Application.Dtos;

public sealed class KnowledgeAccessUpdateDto
{
    public long DocumentId { get; set; }
    public int Visibility { get; set; }
    public string[] AllowedRoles { get; set; } = [];
}

public sealed class KnowledgeAccessDto
{
    public long DocumentId { get; set; }
    public int Visibility { get; set; }
    public string[] AllowedRoles { get; set; } = [];
}
