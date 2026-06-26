using BookTracker.Application;
using BookTracker.Application.Authors;

namespace BookTracker.Web.ViewModels;

// Backs /authors as a compact searchable list. Per-row counts (works,
// books, series) are rolled up onto canonical rows — Stephen King's
// counts include Bachman titles when Bachman is marked as an alias.
// Alias rows show their own counts only.
//
// Detail / rename / alias-management lives at /authors/{id} via
// AuthorDetailViewModel — this VM only renders the listing.
public class AuthorListViewModel(IDispatcher dispatcher)
{
    public bool Loading { get; private set; } = true;
    public IReadOnlyList<AuthorRow> Authors { get; private set; } = [];

    /// <summary>Free-text filter; matches the row name OR any of its alias names so that
    /// typing a pen name surfaces the canonical row even when the alias row is hidden.</summary>
    public string SearchTerm { get; set; } = "";

    /// <summary>When false, only canonical authors render. Defaults to true (show every row).</summary>
    public bool ShowAliases { get; set; } = true;

    /// <summary>Filter applied to <see cref="Authors"/> using <see cref="SearchTerm"/> and <see cref="ShowAliases"/>.</summary>
    public IEnumerable<AuthorRow> FilteredAuthors
    {
        get
        {
            IEnumerable<AuthorRow> q = Authors;

            if (!ShowAliases)
            {
                q = q.Where(a => a.CanonicalAuthorId is null);
            }

            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                var term = SearchTerm.Trim();
                q = q.Where(a =>
                    a.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    a.AliasNames.Any(n => n.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            return q;
        }
    }

    public async Task LoadAsync()
    {
        Loading = true;
        Authors = await dispatcher.Query(new GetAuthorList());
        Loading = false;
    }
}
