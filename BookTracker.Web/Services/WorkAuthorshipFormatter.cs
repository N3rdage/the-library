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
// Sites: BookListViewModel, BookDetailViewModel, AuthorListViewModel,
// SeriesEditViewModel, ShoppingViewModel, HomeViewModel, the Library
// list / BookDetail / Shopping Razor surfaces, and the merge dialogs.
// Anywhere that previously read `work.Author.Name` reads through this
// helper post-cutover.
public static class WorkAuthorshipFormatter
{
    /// <summary>
    /// Format an ordered sequence of author names for display.
    /// Empty input returns "(unknown author)" — defensive; shouldn't happen
    /// post-cutover because every Work has at least one WorkAuthor row.
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
    /// Convenience overload: pull names directly from a Work's WorkAuthors
    /// collection in Order ascending. Caller is responsible for having
    /// loaded WorkAuthors + Author.
    /// </summary>
    public static string Display(Work work) =>
        Display(work.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author.Name));
}
