using XiHan.BasicApp.AI.Domain.Entities;
using XiHan.BasicApp.AI.Domain.Enums;

namespace XiHan.BasicApp.AI.Domain.Policies;

/// <summary>
/// Application-layer ACL: public documents are visible to everyone with read permission;
/// restricted documents require membership in the allowlisted roles (or super-admin).
/// This refines which documents a permitted caller may see — it does not replace the
/// backend read permission check.
/// </summary>
public static class KnowledgeAccessPolicy
{
    public const string SuperAdminRole = "super_admin";

    public static IReadOnlyList<string> ParseRoles(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    public static string NormalizeRoles(IEnumerable<string> roles)
        => string.Join(',', ParseRoles(string.Join(',', roles)));

    public static bool CanView(SysKnowledgeDocument document, Func<string, bool> isInRole)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(isInRole);
        if (document.Visibility == KnowledgeVisibility.Public) return true;
        if (isInRole(SuperAdminRole)) return true;
        var allowed = ParseRoles(document.AllowedRoles);
        return allowed.Count > 0 && allowed.Any(isInRole);
    }
}
