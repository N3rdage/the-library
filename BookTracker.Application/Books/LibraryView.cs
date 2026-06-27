using BookTracker.Data.Models;

namespace BookTracker.Application.Books;

// Shared read-model types for the Library view (/books), relocated from
// BookListViewModel in PR6b-4. The queries that produce them are GetLibraryBooks
// (flat list), GetLibraryGroups (grouped rows), and GetLibraryFilterOptions.

// Series is deliberately NOT a grouping mode: browsing a series is "filter to
// that series + reading-order sort", which the Series filter already gives. The
// grouped Series view was retired once it became a redundant second door to the
// same flat list (TODO #53c).
public enum LibraryGroupBy { None, Author, Genre }

// The Library view's filter criteria, carried from the VM (which owns the bound
// filter state) down to the read queries. For GenreId/SeriesId: 0 = "all";
// -1 = the "uncategorised" bucket (no genre / no series); > 0 = a specific id.
// TagId has no uncategorised bucket, so it's 0 = "all" / > 0 = a specific id.
public record LibraryFilter(
    string? SearchTerm,
    string? Category,
    int GenreId,
    int TagId,
    int SeriesId,
    string? Author,
    BookStatus? Status)
{
    // The single rule for "a specific series is selected", which means a flat,
    // reading-order view: GetLibraryBooks sorts by that series' order, and the
    // VM's ShowingFlatList forces the flat list over any grouping. Both layers
    // call this so the sort trigger and the flat-vs-grouped gate can't drift
    // (e.g. one updated for a multi-series rule and the other not, which would
    // render a single-series view sorted DateAdded-desc instead of reading order).
    public static bool IsSpecificSeries(int seriesId) => seriesId > 0;
}

public record GroupRow(string Key, string Label, int Count)
{
    // The Key for the "(no genre)" bucket — a sentinel distinct from any real id,
    // shared between GetLibraryGroups (which emits it) and the VM's group-drill
    // (which maps it back to the -1 uncategorised filter).
    public const string NoneKey = "_none";
}

public record BookListItem(
    int Id, string Title, string? Subtitle, string Author, string? CoverUrl,
    BookStatus Status, int Rating, int WorkCount, List<string> Genres, List<string> Tags);

public record GenreOption(int Id, string Name, int? ParentGenreId);
public record TagOption(int Id, string Name);
public record SeriesOption(int Id, string Name);
