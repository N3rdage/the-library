namespace BookTracker.Web.Services;

// Normalises and filters raw upstream subject strings before they're handed
// to FuzzyGenreMatch. Keeps the lookup service's lookup paths clean and
// makes the filtering rules unit-testable in isolation.
public static class GenreCandidateCleaner
{
    // Known non-genre subjects that frequently appear in upstream metadata
    // and would otherwise spuriously match a real genre. "Romance languages"
    // is the canonical example — a linguistics category that contains the
    // word "romance".
    private static readonly HashSet<string> DenyList = new(StringComparer.OrdinalIgnoreCase)
    {
        "fiction",
        "fiction in english",
        "english fiction",
        "general",
        "literature",
        "popular literature",
        "romance languages",
        "open_syllabus_project",
        "translations into english",
        "in english",
        "large type books",
    };

    public static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();
        if (trimmed.Length > 80) return null;
        if (DenyList.Contains(trimmed)) return null;
        if (LooksLikeYearOrCentury(trimmed)) return null;

        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static bool LooksLikeYearOrCentury(string s)
    {
        if (s.Length is 4 && s.All(char.IsDigit)) return true; // "1934"
        var lower = s.ToLowerInvariant();
        if (lower.Length is 5 && lower.EndsWith('s') && lower[..4].All(char.IsDigit)) return true; // "1990s"
        if (lower.Contains("century")) return true;
        return false;
    }
}
