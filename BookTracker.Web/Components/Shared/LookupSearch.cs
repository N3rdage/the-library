namespace BookTracker.Web.Components.Shared;

/// <summary>
/// Shared in-memory filter for a <see cref="CreatableAutocomplete"/>'s
/// SearchExisting provider: trim the query, return all names when it's blank,
/// else case-insensitive substring matches, capped at a bounded result count.
/// Centralises the one relevance + cap rule the three lookup typeaheads (series
/// + both publisher sites) share, so a tweak lands in one place and they can't
/// drift apart on ranking or cap.
/// </summary>
internal static class LookupSearch
{
    /// <summary>Bounded result count — keeps an empty-query focus from marshalling
    /// the whole cached list across the Blazor Server circuit at scale.</summary>
    public const int MaxResults = 20;

    public static IEnumerable<string> Filter(IEnumerable<string> names, string? query)
    {
        var q = (query ?? string.Empty).Trim();
        var matches = string.IsNullOrEmpty(q)
            ? names
            : names.Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase));
        return matches.Take(MaxResults);
    }
}
