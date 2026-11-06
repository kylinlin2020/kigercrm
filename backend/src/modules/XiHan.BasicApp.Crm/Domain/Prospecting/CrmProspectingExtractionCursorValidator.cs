namespace XiHan.BasicApp.Crm.Domain.Prospecting;

/// <summary>
/// Shared validation for opaque provider result cursors at both the adapter and persistence boundaries.
/// Null represents the initial/final cursor; non-null values must be bounded and free of control characters.
/// </summary>
public static class CrmProspectingExtractionCursorValidator
{
    public static bool IsValid(string? cursor)
        => cursor is null
            || (cursor.Length <= CrmProspectingExtractionPageValidator.MaxCursorLength
                && !cursor.Any(char.IsControl));
}
