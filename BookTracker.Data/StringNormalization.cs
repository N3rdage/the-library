namespace BookTracker.Data;

public static class StringNormalization
{
    /// <summary>
    /// Trims the string and returns null when it is null or all-whitespace.
    /// The canonical "optional free-text field" normalization used across the
    /// aggregates (ISBN, notes, cover URL, …) so the rule lives in one place
    /// rather than being re-spelled at every assignment.
    /// </summary>
    public static string? TrimToNull(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
