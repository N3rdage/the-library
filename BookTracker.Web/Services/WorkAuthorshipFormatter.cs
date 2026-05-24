using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

// Display-string formatter for multi-author Works. Centralised so every
// read site renders authorship the same way:
//   1 author:  "Preston"
//   2 authors: "Preston & Child"
//   3+:        "Preston, Child, Pendergast"
//
// Matches publishing convention — the ampersand for two reads naturally
// for co-authored books on covers; comma-list for three+ matches what
// anthology spines do.
//
// Post-2026-05-23 Role support: when a Work has non-Author contributors
// (Editor / Translator / Illustrator / etc.) they render after the
// Author-role contributors with a role suffix:
//   "Doug Mauss (editor); Sergio Cariello (illustrator)"
// Author-role contributors lead with no suffix; non-Author contributors
// follow, semicolon-separated, with the lowercase role name in
// parentheses.
//
// Sites: BookListViewModel, BookDetailViewModel, AuthorListViewModel,
// SeriesEditViewModel, ShoppingViewModel, HomeViewModel, the Library
// list / BookDetail / Shopping Razor surfaces, and the merge dialogs.
public static class WorkAuthorshipFormatter
{
    /// <summary>
    /// Format an ordered sequence of author names (Author-role only) for
    /// display. Kept as the simple-string overload for callers that don't
    /// need Role awareness (e.g. plain author rollups). Empty input
    /// returns "(unknown author)".
    /// </summary>
    public static string Display(IEnumerable<string> names)
    {
        var list = names?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? [];
        return list.Count switch
        {
            0 => "(unknown author)",
            1 => list[0],
            2 => $"{list[0]} & {list[1]}",
            _ => string.Join(", ", list),
        };
    }

    /// <summary>
    /// Role-aware overload. Author-role contributors render with the
    /// standard "Preston & Child" treatment; non-Author contributors
    /// append after with a "; Name (role)" suffix.
    /// </summary>
    public static string Display(IEnumerable<(string Name, AuthorRole Role)> contributors)
    {
        var ordered = (contributors ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToList();

        var authors = ordered.Where(c => c.Role == AuthorRole.Author).Select(c => c.Name).ToList();
        var others  = ordered.Where(c => c.Role != AuthorRole.Author).ToList();

        var authorPart = Display(authors);
        if (others.Count == 0) return authorPart;

        var otherPart = string.Join("; ", others.Select(c => $"{c.Name} ({RoleLabel(c.Role)})"));
        return authors.Count == 0 ? otherPart : $"{authorPart}; {otherPart}";
    }

    /// <summary>
    /// Convenience overload: pull contributors directly from a Work's
    /// WorkAuthors collection, sorted by (Role, Order) so Author-role
    /// contributors come first then other roles each in their own Order
    /// sequence. Caller is responsible for having loaded WorkAuthors +
    /// Author.
    /// </summary>
    public static string Display(Work work) =>
        Display(work.WorkAuthors
            .OrderBy(wa => wa.Role == AuthorRole.Author ? 0 : 1) // Author first
            .ThenBy(wa => (int)wa.Role)                          // then other roles in enum order
            .ThenBy(wa => wa.Order)
            .Select(wa => (wa.Author.Name, wa.Role)));

    /// <summary>
    /// Single-string "primary contributor" for snapshot / rollup surfaces
    /// that want one by-line per Book or Work. Returns the lead Author-
    /// role contributor's bare name, or for editor-only Works (dictionaries
    /// etc.) the lowest-Order non-Author contributor with role suffix —
    /// e.g. "Doug Mauss (editor)" — so the by-line conveys "edited by X"
    /// rather than a misleading "(unknown)". Caller passes the
    /// contributors pre-sorted Author-first then by (Role, Order); this
    /// helper just picks the head.
    /// </summary>
    public static string DisplayPrimary(IEnumerable<(string Name, AuthorRole Role)> sortedContributors)
    {
        var first = (sortedContributors ?? [])
            .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Name));
        if (string.IsNullOrWhiteSpace(first.Name)) return "(unknown)";
        return first.Role == AuthorRole.Author
            ? first.Name
            : $"{first.Name} ({RoleLabel(first.Role)})";
    }

    private static string RoleLabel(AuthorRole role) => role switch
    {
        AuthorRole.Editor      => "editor",
        AuthorRole.Translator  => "translator",
        AuthorRole.Illustrator => "illustrator",
        AuthorRole.Adaptor     => "adaptor",
        AuthorRole.Compiler    => "compiler",
        AuthorRole.Foreword    => "foreword",
        AuthorRole.Contributor => "contributor",
        _                      => role.ToString().ToLowerInvariant(),
    };
}
