using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XiHan.BasicApp.AI.Domain.Policies;

public sealed record KnowledgeDuplicateGroup(string Hash, IReadOnlyList<long> DocumentIds, IReadOnlyList<string> Titles);

/// <summary>
/// Deterministic tag normalisation, content hashing and duplicate grouping for the knowledge bank.
/// No embedding or external service is involved: duplicates are byte-identical raw content only.
/// </summary>
public static class KnowledgeTagPolicy
{
    public const int MaximumTags = 20;

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return tags.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length is > 0 and <= 40)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(MaximumTags)
            .ToArray();
    }

    public static IReadOnlyList<string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Knowledge tags must be a JSON array.", nameof(json));
        return NormalizeTags(document.RootElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty));
    }

    public static string HashContent(string rawContent)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawContent ?? string.Empty)))
            .ToLowerInvariant();

    public static IReadOnlyList<KnowledgeDuplicateGroup> FindDuplicates(
        IReadOnlyList<(long Id, string Title, string? Hash)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return documents
            .Where(item => !string.IsNullOrWhiteSpace(item.Hash))
            .GroupBy(item => item.Hash!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new KnowledgeDuplicateGroup(
                group.Key,
                group.Select(item => item.Id).ToArray(),
                group.Select(item => item.Title).ToArray()))
            .OrderBy(group => group.Hash, StringComparer.Ordinal)
            .ToArray();
    }
}