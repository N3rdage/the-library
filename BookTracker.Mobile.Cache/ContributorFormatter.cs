using BookTracker.Shared.Catalog;

namespace BookTracker.Mobile.Cache;

// Mirror of the Web's WorkAuthorshipFormatter.Display role-aware
// overload — kept here so the Mobile project can render the same
// "Tolkien & Child; Sergio Cariello (illustrator)" by-line without
// pulling BookTracker.Data into the mobile build. AuthorContribution.Role
// is a string ("Author" / "Editor" / "Translator" / ...) on the
// wire, so no enum conversion is needed.
//
// Lives in Mobile.Cache rather than Mobile so it sits alongside the
// AuthorContribution-shaped data the cache round-trips, and so the
// unit tests in BookTracker.Mobile.Cache.Tests can cover it without
// dragging in the MAUI runtime.
public static class ContributorFormatter
{
    /// <summary>Format a contributor list into the by-line shape used
    /// on the Web (single name / "A &amp; B" / "A, B, C" for Authors;
    /// then "; Name (role)" suffixes for each non-Author). Returns
    /// "" for an empty input so the caller can decide on a fallback
    /// (typically the snapshot's PrimaryAuthor string).</summary>
    public static string Format(IReadOnlyList<AuthorContribution>? contributors)
    {
        if (contributors is null || contributors.Count == 0) return "";

        var authors = contributors
            .Where(c => string.Equals(c.Role, "Author", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        var others = contributors
            .Where(c => !string.Equals(c.Role, "Author", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToList();

        var authorPart = authors.Count switch
        {
            0 => "",
            1 => authors[0],
            2 => $"{authors[0]} & {authors[1]}",
            _ => string.Join(", ", authors),
        };

        if (others.Count == 0) return authorPart;
        var otherPart = string.Join("; ",
            others.Select(c => $"{c.Name} ({c.Role.ToLowerInvariant()})"));
        return string.IsNullOrEmpty(authorPart) ? otherPart : $"{authorPart}; {otherPart}";
    }
}
