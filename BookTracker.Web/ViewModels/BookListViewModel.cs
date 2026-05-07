using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public enum LibraryGroupBy { None, Author, Genre, Collection }

public class BookListViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public const int PageSize = 20;

    public bool Loading { get; private set; } = true;

    // Flat list (used only when GroupBy == None).
    public List<BookListItem> Books { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; }

    // Grouped view state. The group list is loaded up front (with counts)
    // and rendered as a collapsible accordion. Each group's books are
    // fetched lazily on first expand and paged independently.
    public LibraryGroupBy SelectedGroupBy { get; set; } = LibraryGroupBy.Author;
    public List<GroupRow> Groups { get; private set; } = [];
    public Dictionary<string, GroupBooks> LoadedGroups { get; } = [];
    public HashSet<string> ExpandedGroupKeys { get; } = [];

    public string SearchTerm { get; set; } = "";
    public string SelectedCategory { get; set; } = "";
    public int SelectedGenreId { get; set; }
    public int SelectedTagId { get; set; }
    public string SelectedAuthor { get; set; } = "";

    public List<GenreOption> AllGenres { get; private set; } = [];
    public List<TagOption> AllTags { get; private set; } = [];
    public List<string> AllAuthors { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await LoadFilterOptionsAsync();
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        if (SelectedGroupBy == LibraryGroupBy.None)
        {
            await LoadBooksAsync();
        }
        else
        {
            await LoadGroupsAsync();
        }
    }

    private async Task LoadFilterOptionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var genres = await db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new GenreOption(g.Id, g.Name, g.ParentGenreId))
            .ToListAsync();

        var topLevel = genres.Where(g => g.ParentGenreId is null).OrderBy(g => g.Name).ToList();
        AllGenres = [];
        foreach (var parent in topLevel)
        {
            AllGenres.Add(parent);
            var children = genres.Where(g => g.ParentGenreId == parent.Id).OrderBy(g => g.Name);
            AllGenres.AddRange(children);
        }

        AllTags = await db.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagOption(t.Id, t.Name))
            .ToListAsync();

        // Author dropdown lists every Author entity (including pen names) by
        // name so the user can filter by either the canonical or an alias.
        AllAuthors = await db.Authors
            .OrderBy(a => a.Name)
            .Select(a => a.Name)
            .ToListAsync();
    }

    public async Task LoadBooksAsync()
    {
        Loading = true;
        await using var db = await dbFactory.CreateDbContextAsync();

        var query = ApplyFilters(BookQueryWithIncludes(db));

        TotalCount = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;

        var raw = await query
            .OrderByDescending(b => b.DateAdded)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        Books = raw.Select(ToBookListItem).ToList();
        Loading = false;
    }

    public async Task LoadGroupsAsync()
    {
        Loading = true;
        Groups = [];
        LoadedGroups.Clear();
        ExpandedGroupKeys.Clear();

        await using var db = await dbFactory.CreateDbContextAsync();
        var filtered = ApplyFilters(BookQueryWithIncludes(db));

        // Each grouping reduces the filtered set into (Key, Label, Count)
        // rows. Books with no grouping value (no genre, no series) bucket
        // into an explicit "(none)" row at the end.
        Groups = SelectedGroupBy switch
        {
            LibraryGroupBy.Author => await GroupByAuthorAsync(db, filtered),
            LibraryGroupBy.Genre => await GroupByGenreAsync(db, filtered),
            LibraryGroupBy.Collection => await GroupBySeriesAsync(db, filtered),
            _ => [],
        };

        Loading = false;
    }

    public async Task ToggleGroupAsync(string key)
    {
        if (ExpandedGroupKeys.Contains(key))
        {
            ExpandedGroupKeys.Remove(key);
            return;
        }

        ExpandedGroupKeys.Add(key);
        if (!LoadedGroups.ContainsKey(key))
        {
            await LoadGroupBooksAsync(key, page: 1);
        }
    }

    public async Task LoadGroupBooksAsync(string key, int page)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var filtered = ApplyFilters(BookQueryWithIncludes(db));
        filtered = ApplyGroupFilter(filtered, key);

        var total = await filtered.CountAsync();

        // Series-aware sort inside a group expand. Two shapes:
        //   - Collection group: every book in the group belongs to that
        //     specific series, so sort by the matching Work's SeriesOrder.
        //     Compendiums take the minimum.
        //   - Author group: books span any series the author wrote in.
        //     Cluster series-having books first (alphabetical by series
        //     name, then SeriesOrder), then standalone books by title.
        // Genre / (no-series) / (no-genre) buckets fall back to title sort.
        IQueryable<Book> ordered;
        if (SelectedGroupBy == LibraryGroupBy.Collection
            && key != NoneKey
            && int.TryParse(key, out var seriesIdForSort))
        {
            ordered = filtered
                .OrderBy(b => b.Works
                    .Where(w => w.SeriesId == seriesIdForSort)
                    .Min(w => (int?)w.SeriesOrder) ?? int.MaxValue)
                .ThenBy(b => b.Title);
        }
        else if (SelectedGroupBy == LibraryGroupBy.Author && key != NoneKey)
        {
            ordered = filtered
                .OrderBy(b => b.Works.Any(w => w.SeriesId != null) ? 0 : 1)
                .ThenBy(b => b.Works
                    .Where(w => w.SeriesId != null)
                    .Select(w => w.Series!.Name)
                    .Min())
                .ThenBy(b => b.Works
                    .Where(w => w.SeriesId != null)
                    .Min(w => (int?)w.SeriesOrder) ?? int.MaxValue)
                .ThenBy(b => b.Title);
        }
        else
        {
            ordered = filtered.OrderBy(b => b.Title);
        }

        var raw = await ordered
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var items = raw.Select(ToBookListItem).ToList();
        LoadedGroups[key] = new GroupBooks(items, page, total);
    }

    private IQueryable<Book> ApplyGroupFilter(IQueryable<Book> q, string key)
    {
        if (key == NoneKey)
        {
            return SelectedGroupBy switch
            {
                LibraryGroupBy.Genre => q.Where(b => !b.Works.Any(w => w.Genres.Any())),
                LibraryGroupBy.Collection => q.Where(b => !b.Works.Any(w => w.SeriesId.HasValue)),
                _ => q, // Author always has a value since Work.AuthorId is non-null
            };
        }

        if (!int.TryParse(key, out var id)) return q;

        return SelectedGroupBy switch
        {
            // Author key is the CANONICAL author id — match any Work whose
            // Authors include the canonical OR an alias of it.
            LibraryGroupBy.Author => q.Where(b => b.Works.Any(w =>
                w.Authors.Any(a => a.Id == id || a.CanonicalAuthorId == id))),
            LibraryGroupBy.Genre => q.Where(b => b.Works.Any(w => w.Genres.Any(g => g.Id == id))),
            LibraryGroupBy.Collection => q.Where(b => b.Works.Any(w => w.SeriesId == id)),
            _ => q,
        };
    }

    private async Task<List<GroupRow>> GroupByAuthorAsync(BookTrackerDbContext db, IQueryable<Book> filtered)
    {
        // Roll up by canonical author id (CanonicalAuthorId ?? Id) so a
        // Bachman title appears under King. Co-authored works expand into
        // one row per credited author — Preston + Child appears under both
        // canonicals (post-PR2 behaviour change vs the lead-only legacy).
        var raw = await filtered
            .SelectMany(b => b.Works.SelectMany(w => w.Authors.Select(a => new
            {
                BookId = b.Id,
                CanonicalId = a.CanonicalAuthorId ?? a.Id,
            })))
            .Distinct() // Avoid double-counting a book whose two Works share a canonical author.
            .GroupBy(x => x.CanonicalId)
            .Select(g => new { CanonicalId = g.Key, Count = g.Count() })
            .ToListAsync();

        var canonicalIds = raw.Select(r => r.CanonicalId).ToList();
        var names = await db.Authors
            .Where(a => canonicalIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        return raw
            .Select(r => new GroupRow(
                Key: r.CanonicalId.ToString(),
                Label: names.GetValueOrDefault(r.CanonicalId) ?? "(unknown)",
                Count: r.Count))
            .OrderBy(g => g.Label)
            .ToList();
    }

    private async Task<List<GroupRow>> GroupByGenreAsync(BookTrackerDbContext db, IQueryable<Book> filtered)
    {
        var raw = await filtered
            .SelectMany(b => b.Works.SelectMany(w => w.Genres.Select(g => new { BookId = b.Id, GenreId = g.Id })))
            .Distinct()
            .GroupBy(x => x.GenreId)
            .Select(g => new { GenreId = g.Key, Count = g.Count() })
            .ToListAsync();

        var genreIds = raw.Select(r => r.GenreId).ToList();
        var names = await db.Genres
            .Where(g => genreIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        var groups = raw
            .Select(r => new GroupRow(
                Key: r.GenreId.ToString(),
                Label: names.GetValueOrDefault(r.GenreId) ?? "(unknown)",
                Count: r.Count))
            .OrderBy(g => g.Label)
            .ToList();

        var ungenredCount = await filtered.CountAsync(b => !b.Works.Any(w => w.Genres.Any()));
        if (ungenredCount > 0)
        {
            groups.Add(new GroupRow(NoneKey, "(no genre)", ungenredCount));
        }
        return groups;
    }

    private async Task<List<GroupRow>> GroupBySeriesAsync(BookTrackerDbContext db, IQueryable<Book> filtered)
    {
        var raw = await filtered
            .SelectMany(b => b.Works
                .Where(w => w.SeriesId.HasValue)
                .Select(w => new { BookId = b.Id, SeriesId = w.SeriesId!.Value }))
            .Distinct()
            .GroupBy(x => x.SeriesId)
            .Select(g => new { SeriesId = g.Key, Count = g.Count() })
            .ToListAsync();

        var seriesIds = raw.Select(r => r.SeriesId).ToList();
        var names = await db.Series
            .Where(s => seriesIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        var groups = raw
            .Select(r => new GroupRow(
                Key: r.SeriesId.ToString(),
                Label: names.GetValueOrDefault(r.SeriesId) ?? "(unknown)",
                Count: r.Count))
            .OrderBy(g => g.Label)
            .ToList();

        var unseriesedCount = await filtered.CountAsync(b => !b.Works.Any(w => w.SeriesId.HasValue));
        if (unseriesedCount > 0)
        {
            groups.Add(new GroupRow(NoneKey, "(no series)", unseriesedCount));
        }
        return groups;
    }

    private IQueryable<Book> BookQueryWithIncludes(BookTrackerDbContext db) => db.Books
        .Include(b => b.Tags)
        .Include(b => b.Works).ThenInclude(w => w.Genres)
        .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author);

    private IQueryable<Book> ApplyFilters(IQueryable<Book> query)
    {
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(b =>
                b.Title.Contains(term) ||
                b.Works.Any(w => w.Title.Contains(term) || w.Authors.Any(a => a.Name.Contains(term))));
        }

        if (!string.IsNullOrEmpty(SelectedCategory) && Enum.TryParse<BookCategory>(SelectedCategory, out var cat))
        {
            query = query.Where(b => b.Category == cat);
        }

        if (SelectedGenreId > 0)
        {
            query = query.Where(b => b.Works.Any(w => w.Genres.Any(g => g.Id == SelectedGenreId)));
        }

        if (SelectedTagId > 0)
        {
            query = query.Where(b => b.Tags.Any(t => t.Id == SelectedTagId));
        }

        if (!string.IsNullOrWhiteSpace(SelectedAuthor))
        {
            var author = SelectedAuthor.Trim();
            query = query.Where(b => b.Works.Any(w =>
                w.Authors.Any(a =>
                    a.Name == author ||
                    (a.CanonicalAuthor != null && a.CanonicalAuthor.Name == author))));
        }

        return query;
    }

    private static BookListItem ToBookListItem(Book b) => new(
        b.Id,
        b.Title,
        // Subtitle only renders for single-Work books — for collections (Works
        // > 1) the inner-Work subtitle would be the subtitle of an arbitrary
        // story, which reads as data noise in the list. Collections surface
        // their multi-Work-ness via the WorkCount indicator below the title.
        b.Works.Count == 1 ? b.Works.First().Subtitle : null,
        // Comma-join unique author names across all Works on this Book. For a
        // single-Work co-authored book this renders "Preston, Child" rather
        // than the prettier "Preston & Child" — list views stay uniform; the
        // " & " formatter is reserved for single-book / single-Work surfaces
        // (BookDetail, dialogs).
        string.Join(", ", b.Works.SelectMany(w => w.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author.Name)).Distinct()),
        b.DefaultCoverArtUrl,
        b.Status,
        b.Rating,
        b.Works.Count,
        b.Works.SelectMany(w => w.Genres).Select(g => g.Name).Distinct().ToList(),
        b.Tags.Select(t => t.Name).ToList());

    public async Task ApplyFiltersAsync()
    {
        CurrentPage = 1;
        await ReloadAsync();
    }

    public async Task ClearFiltersAsync()
    {
        SearchTerm = "";
        SelectedCategory = "";
        SelectedGenreId = 0;
        SelectedTagId = 0;
        SelectedAuthor = "";
        CurrentPage = 1;
        await ReloadAsync();
    }

    public async Task GoToPageAsync(int page)
    {
        if (page < 1 || page > TotalPages) return;
        CurrentPage = page;
        await LoadBooksAsync();
    }

    public async Task ChangeGroupingAsync(LibraryGroupBy newGroupBy)
    {
        SelectedGroupBy = newGroupBy;
        CurrentPage = 1;
        await ReloadAsync();
    }

    public static string StatusBadgeClass(BookStatus status) => status switch
    {
        BookStatus.Reading => "bg-primary",
        BookStatus.Read => "bg-success",
        BookStatus.Unread => "bg-secondary",
        _ => "bg-secondary"
    };

    public const string NoneKey = "_none";

    public record GroupRow(string Key, string Label, int Count);
    public record GroupBooks(List<BookListItem> Books, int Page, int TotalCount)
    {
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    }

    public record BookListItem(
        int Id, string Title, string? Subtitle, string Author, string? CoverUrl,
        BookStatus Status, int Rating, int WorkCount, List<string> Genres, List<string> Tags);

    public record GenreOption(int Id, string Name, int? ParentGenreId);
    public record TagOption(int Id, string Name);
}
