namespace XiHan.BasicApp.AI.Application.Dtos;

public sealed class KnowledgeTagsUpdateDto
{
    public long DocumentId { get; set; }
    public string[] Tags { get; set; } = [];
}

public sealed class KnowledgeTagItemDto
{
    public string Tag { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
}

public sealed class KnowledgeDuplicateGroupDto
{
    public string Hash { get; set; } = string.Empty;
    public long[] DocumentIds { get; set; } = [];
    public string[] Titles { get; set; } = [];
}

public sealed class KnowledgeDuplicateReportDto
{
    public int Groups { get; set; }
    public int DuplicateDocuments { get; set; }
    public IReadOnlyList<KnowledgeDuplicateGroupDto> Items { get; set; } = [];
}
