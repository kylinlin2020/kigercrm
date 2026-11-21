using System.Text.Json;
using XiHan.BasicApp.Crm.Domain.Entities;
using XiHan.BasicApp.Crm.Domain.Entities.Enums;

namespace XiHan.BasicApp.Crm.Domain.Prospecting;

public sealed record CrmExamSelectionRule(string Category, int Count);

/// <summary>
/// Deterministic paper composition: pick the first N questions of each category by ascending id.
/// The selected ids are frozen on the paper; later bank edits never change an existing paper.
/// </summary>
public static class CrmExamSelectionPolicy
{
    public const int MaximumSelectedQuestions = 50;

    public static IReadOnlyList<CrmExamSelectionRule> ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Selection rules must be a JSON array.", nameof(json));
        var result = new List<CrmExamSelectionRule>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Each selection rule must be an object.", nameof(json));
            string? category = null;
            if (element.TryGetProperty("category", out var categoryValue)
                && categoryValue.ValueKind == JsonValueKind.String)
                category = categoryValue.GetString()?.Trim();
            var count = element.TryGetProperty("count", out var countValue)
                && countValue.ValueKind == JsonValueKind.Number
                && countValue.TryGetInt32(out var parsed)
                ? parsed
                : 0;
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Selection rule needs a category.", nameof(json));
            if (count < 1 || count > MaximumSelectedQuestions)
                throw new ArgumentOutOfRangeException(nameof(json), "Rule count must be 1..50.");
            result.Add(new CrmExamSelectionRule(category!, count));
        }
        if (result.Count == 0) throw new ArgumentException("Selection rules must not be empty.", nameof(json));
        if (result.Sum(rule => rule.Count) > MaximumSelectedQuestions)
            throw new ArgumentException("Selection rules must select at most 50 questions in total.", nameof(json));
        if (result.Select(rule => rule.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new ArgumentException("Selection rule categories must be unique.", nameof(json));
        return result;
    }

    public static IReadOnlyList<long> Select(
        IReadOnlyList<CrmExamSelectionRule> rules,
        IReadOnlyList<SysCrmExamQuestion> bank,
        bool randomize = false,
        int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(bank);
        if (rules.Count == 0) throw new ArgumentException("Selection rules are required.", nameof(rules));
        if (randomize && !seed.HasValue)
            throw new ArgumentException("A seed is required for randomised selection.", nameof(seed));
        var selected = new List<long>();
        foreach (var rule in rules)
        {
            var matches = bank.Where(question =>
                    string.Equals(Normalize(question.Category), Normalize(rule.Category), StringComparison.OrdinalIgnoreCase))
                .Select(question => question.BasicId)
                .OrderBy(id => id)
                .ToArray();
            if (matches.Length < rule.Count)
                throw new InvalidOperationException($"Category '{rule.Category}' has fewer than {rule.Count} questions.");
            if (randomize) Shuffle(matches, CategorySeed(seed!.Value, rule.Category));
            selected.AddRange(matches.Take(rule.Count));
        }
        if (selected.Distinct().Count() != selected.Count)
            throw new InvalidOperationException("Selection produced duplicate question ids.");
        return selected;
    }

    /// <summary>Fisher-Yates shuffle driven by a derived seed; same seed and ids give the same order.</summary>
    public static void Shuffle(long[] ids, int seed)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var random = new Random(seed);
        for (var index = ids.Length - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (ids[index], ids[swap]) = (ids[swap], ids[index]);
        }
    }

    // FNV-1a 32-bit is deterministic across processes/runs (unlike HashCode, which is seeded).
    private static int CategorySeed(int seed, string category)
    {
        unchecked
        {
            uint hash = 2166136261;
            var text = $"{seed}:{Normalize(category)}";
            foreach (var character in text)
            {
                hash ^= (uint)char.ToLowerInvariant(character);
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}