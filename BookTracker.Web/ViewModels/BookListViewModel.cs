using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

// View model for the Library page (/books). As of PR6b-4 it's fully off
// DbContext: the reads dispatch GetLibraryFilterOptions / GetLibraryBooks /
// GetLibraryGroups, and the inline writes dispatch SetBookStatus / MarkBookRead /
// RateBook. The VM owns presentation + URL state only — the bound filter fields,
// pagination, the filter ⇄ query-string mapping, and the group-drill routing.
public class BookListViewModel(IDispatcher dispatcher)
{
    // Mirrors the page size the read handler paginates by, so the page's
    // "showing X–Y of Z" label keys off one source of truth.
    public const int PageSize = GetLibraryBooks.PageSize;

    public bool Loading { get; private set; } = true;

    // Flat list (used only when GroupBy == None).
    public List<BookListItem> Books { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; private set; }

    // Grouped view state: the group list is loaded up front (with counts) and
    // rendered as a virtualized list of rows. Clicking a row drills into a flat,
    // filtered book list (see BuildGroupDrillParameters) rather than expanding
    // in place.
    public LibraryGroupBy SelectedGroupBy { get; set; } = LibraryGroupBy.Author;
    public List<GroupRow> Groups { get; private set; } = [];

    public string SearchTerm { get; set; } = "";
    public string SelectedCategory { get; set; } = "";
    // 0 = all; > 0 = that genre / series; -1 = the "uncategorised" bucket
    // (books with no genre / no series). The -1 sentinel lets the group
    // drill-down and the filter dropdowns target uncategorised books.
    public int SelectedGenreId { get; set; }
    public int SelectedTagId { get; set; }
    public int SelectedSeriesId { get; set; }
    public string SelectedAuthor { get; set; } = "";
    // Null = all statuses. Backs the Library status filter, which doubles as
    // the re-triage worklist (filter to Unread, work down the list).
    public BookStatus? SelectedStatus { get; set; }

    // True when the view renders as a flat book list rather than grouped rows.
    // A *specific* series filter (SelectedSeriesId > 0) always forces the flat,
    // reading-order list regardless of the Group-by selection — that's the
    // replacement for the retired Series grouping (TODO #53c): picking a series
    // shows it in reading order, not a near-useless single-series author/genre
    // grouping. Clearing the series filter reverts to the chosen grouping. The
    // page render branch + paging clamp key off this same property so the
    // loaded shape and the rendered shape can't disagree.
    public bool ShowingFlatList => SelectedGroupBy == LibraryGroupBy.None || LibraryFilter.IsSpecificSeries(SelectedSeriesId);

    public List<GenreOption> AllGenres { get; private set; } = [];
    public List<TagOption> AllTags { get; private set; } = [];
    public List<SeriesOption> AllSeries { get; private set; } = [];
    public List<string> AllAuthors { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await LoadFilterOptionsAsync();
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (ShowingFlatList)
        {
            await LoadBooksAsync();
        }
        else
        {
            await LoadGroupsAsync();
        }
    }

    public async Task LoadFilterOptionsAsync()
    {
        var options = await dispatcher.Query(new GetLibraryFilterOptions());
        AllGenres = options.Genres.ToList();
        AllTags = options.Tags.ToList();
        AllSeries = options.Series.ToList();
        AllAuthors = options.Authors.ToList();
    }

    public async Task LoadBooksAsync()
    {
        Loading = true;
        var result = await dispatcher.Query(new GetLibraryBooks(CurrentFilter(), CurrentPage));
        Books = result.Books.ToList();
        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
        // The handler clamps an out-of-range page; reflect the effective page so
        // OnParametersSetAsync can write the correction back to the URL.
        CurrentPage = result.Page;
        Loading = false;
    }

    public async Task LoadGroupsAsync()
    {
        Loading = true;
        Groups = (await dispatcher.Query(new GetLibraryGroups(CurrentFilter(), SelectedGroupBy))).ToList();
        Loading = false;
    }

    // Snapshot the bound filter fields into the query contract.
    private LibraryFilter CurrentFilter() => new(
        SearchTerm, SelectedCategory, SelectedGenreId, SelectedTagId, SelectedSeriesId, SelectedAuthor, SelectedStatus);

    // Filter state ⇄ query string. The URL is the source of truth for the
    // Library view: every filter, the grouping, and the flat-list page live in
    // the query so a drill into a book + browser Back restores the exact view
    // (the VM is Transient — without this, every return rebuilds with defaults).
    // Defaults are omitted from the URL to keep it clean: no `group` means the
    // default Author grouping; no `page` means page 1.
    public Dictionary<string, object?> ToQueryParameters() => new()
    {
        ["q"] = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm.Trim(),
        ["group"] = SelectedGroupBy == LibraryGroupBy.Author ? null : SelectedGroupBy.ToString(),
        ["category"] = string.IsNullOrEmpty(SelectedCategory) ? null : SelectedCategory,
        ["genre"] = SentinelToQuery(SelectedGenreId),
        ["tag"] = SelectedTagId > 0 ? SelectedTagId : (int?)null,
        ["series"] = SentinelToQuery(SelectedSeriesId),
        ["status"] = SelectedStatus?.ToString(),
        ["author"] = string.IsNullOrWhiteSpace(SelectedAuthor) ? null : SelectedAuthor.Trim(),
        ["page"] = CurrentPage > 1 ? CurrentPage : (int?)null,
    };

    // Inverse of ToQueryParameters: hydrate the filter fields from raw query
    // values. Unparseable / absent values fall back to the same defaults the
    // serializer omits, so round-tripping an omitted param is lossless.
    public void ApplyQueryParameters(
        string? q, string? group, string? category,
        int? genre, int? tag, int? series, string? status, string? author, int? page)
    {
        SearchTerm = q ?? "";
        SelectedGroupBy = Enum.TryParse<LibraryGroupBy>(group, ignoreCase: true, out var g)
            ? g : LibraryGroupBy.Author;
        SelectedCategory = category ?? "";
        SelectedGenreId = SentinelFromQuery(genre);
        SelectedTagId = tag is > 0 ? tag.Value : 0;
        SelectedSeriesId = SentinelFromQuery(series);
        SelectedStatus = Enum.TryParse<BookStatus>(status, ignoreCase: true, out var s)
            ? s : null;
        SelectedAuthor = author ?? "";
        CurrentPage = page is > 0 ? page.Value : 1;
    }

    // Sentinel ⇄ query for the genre/series filters: 0 = "all" (omitted from the
    // URL); -1 = the "uncategorised" bucket ("(no genre)" / "(no series)"); > 0 =
    // a specific id. One rule, named once per direction rather than inlined at
    // each call site. (Tag has no uncategorised bucket, so it stays `> 0`.)
    private static int? SentinelToQuery(int value) => value != 0 ? value : null;
    private static int SentinelFromQuery(int? value) => value is -1 or > 0 ? value.Value : 0;

    // Build the query parameters for drilling a group row into a flat, filtered
    // book list: carry the current filters forward, switch to the flat list,
    // reset paging, and pin the clicked group's dimension. Lives on the VM (not
    // the page) so it's unit-testable and so the NoneKey/id key-encoding stays
    // next to the grouping code that produces those keys.
    public Dictionary<string, object?> BuildGroupDrillParameters(GroupRow group)
    {
        var dict = ToQueryParameters();
        // Must be the explicit "None" token, NOT null: an omitted group hydrates
        // back to the Author default, which would land on a grouped view, not
        // the flat list.
        dict["group"] = LibraryGroupBy.None.ToString();
        dict["page"] = null;

        switch (SelectedGroupBy)
        {
            case LibraryGroupBy.Author:
                // group.Label is the (unique) canonical author name, which the
                // flat-list author filter matches including alias rollup.
                dict["author"] = group.Label;
                break;
            case LibraryGroupBy.Genre:
                dict["genre"] = group.Key == GroupRow.NoneKey ? -1 : int.Parse(group.Key);
                break;
            // None never reaches here — drill is only invoked from rendered
            // group rows, which exist only when grouping is Author/Genre. A new
            // grouping mode must add its own arm or this throws, rather than
            // silently emitting a drill with no dimension pinned (= show
            // everything).
            default:
                throw new InvalidOperationException(
                    $"No group-drill mapping for grouping mode {SelectedGroupBy}.");
        }

        return dict;
    }

    public async Task ApplyFiltersAsync()
    {
        CurrentPage = 1;
        await ReloadAsync();
    }

    /// <summary>Inline status set from the Library row's status dropdown. The
    /// loaded row is patched in place rather than re-queried, so a book the user
    /// just moved out of the active status filter doesn't vanish mid-interaction
    /// — the filter re-applies only on the next explicit reload (filter change /
    /// paging / navigation).</summary>
    public async Task SetStatusAsync(int bookId, BookStatus status)
    {
        await dispatcher.Send(new SetBookStatus(bookId, status));
        PatchLoadedItem(bookId, item => item with { Status = status });
    }

    /// <summary>Mark-Read quick action: status + rating + notes in one gesture
    /// (the Mark-Read dialog supplies rating + notes). A distinct user intention
    /// from the plain status set above, so it's its own command — not three
    /// field updates (convention C10). A null <paramref name="notes"/> leaves
    /// existing notes intact.</summary>
    public async Task MarkReadAsync(int bookId, int rating, string? notes)
    {
        await dispatcher.Send(new MarkBookRead(bookId, rating, notes));
        PatchLoadedItem(bookId, item => item with { Status = BookStatus.Read, Rating = rating });
    }

    /// <summary>Inline rating set from the Library row (independent of status —
    /// rating changes never hide a row, since there's no rating filter).</summary>
    public async Task SetRatingAsync(int bookId, int rating)
    {
        await dispatcher.Send(new RateBook(bookId, rating));
        PatchLoadedItem(bookId, item => item with { Rating = rating });
    }

    // Replace the matching row in the flat list (records are immutable) so an
    // inline status/rating change shows immediately without a re-query.
    private void PatchLoadedItem(int bookId, Func<BookListItem, BookListItem> transform)
    {
        var flatIdx = Books.FindIndex(b => b.Id == bookId);
        if (flatIdx >= 0) Books[flatIdx] = transform(Books[flatIdx]);
    }

    public static string StatusBadgeClass(BookStatus status) => status switch
    {
        BookStatus.Reading => "bg-primary",
        BookStatus.Read => "bg-success",
        BookStatus.Unread => "bg-secondary",
        BookStatus.Reference => "bg-info",
        _ => "bg-secondary"
    };
}
