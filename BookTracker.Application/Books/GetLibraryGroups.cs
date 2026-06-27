using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model for the Library grouped view — reduces the filtered set into
// (Key, Label, Count) rows by author or genre. Relocated from BookListViewModel's
// GroupBy* methods in PR6b-4. A specific series filter never reaches here (it
// forces the flat list via ShowingFlatList), so there's no "(no series)" bucket.
public sealed record GetLibraryGroups(LibraryFilter Filter, LibraryGroupBy GroupBy)
    : IQuery<IReadOnlyList<GroupRow>>;

public sealed class GetLibraryGroupsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetLibraryGroups, IReadOnlyList<GroupRow>>
{
    public async Task<IReadOnlyList<GroupRow>> HandleAsync(GetLibraryGroups query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var filtered = LibraryBookQuery.Filtered(db, query.Filter);

        return query.GroupBy switch
        {
            LibraryGroupBy.Author => await GroupByAuthorAsync(db, filtered, query.Filter, ct),
            LibraryGroupBy.Genre => await GroupByGenreAsync(db, filtered, ct),
            _ => [],
        };
    }

    private static async Task<List<GroupRow>> GroupByAuthorAsync(
        BookTrackerDbContext db, IQueryable<Book> filtered, LibraryFilter f, CancellationToken ct)
    {
        // Filter-aware grouping: a narrowed set (explicit author filter, or a
        // title search hitting a compendium) groups by a single / primary author
        // rather than fanning out across every credited co-author.
        if (!string.IsNullOrWhiteSpace(f.Author))
            return await SingleAuthorGroupAsync(db, filtered, f.Author.Trim(), ct);

        if (!string.IsNullOrWhiteSpace(f.SearchTerm))
            return await PrimaryAuthorGroupAsync(db, filtered, ct);

        // Default: fan out across all credited authors, rolling aliases up to
        // canonicals (a Bachman title appears under King; a Preston + Child
        // co-authored work appears under both).
        var raw = await filtered
            .SelectMany(b => b.Works.SelectMany(w => w.Authors.Select(a => new
            {
                BookId = b.Id,
                CanonicalId = a.CanonicalAuthorId ?? a.Id,
            })))
            .Distinct() // Avoid double-counting a book whose two Works share a canonical author.
            .GroupBy(x => x.CanonicalId)
            .Select(g => new { CanonicalId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var names = await AuthorNamesAsync(db, raw.Select(r => r.CanonicalId), ct);
        return ToGroupRows(raw.Select(r => (r.CanonicalId, r.Count)), names);
    }

    private static async Task<List<GroupRow>> SingleAuthorGroupAsync(
        BookTrackerDbContext db, IQueryable<Book> filtered, string selectedName, CancellationToken ct)
    {
        // Resolve the typed name (possibly an alias, e.g. "Richard Bachman") to
        // its canonical, so the single group row is keyed + labelled by the
        // canonical (King), matching the unfiltered fan-out's rollup.
        var matched = await db.Authors
            .Where(a => a.Name == selectedName)
            .Select(a => new
            {
                CanonicalId = a.CanonicalAuthorId ?? a.Id,
                CanonicalName = a.CanonicalAuthor != null ? a.CanonicalAuthor.Name : a.Name,
            })
            .FirstOrDefaultAsync(ct);

        if (matched is null) return [];

        var count = await filtered.CountAsync(ct);
        return count == 0
            ? []
            : [new GroupRow(matched.CanonicalId.ToString(), matched.CanonicalName, count)];
    }

    private static async Task<List<GroupRow>> PrimaryAuthorGroupAsync(
        BookTrackerDbContext db, IQueryable<Book> filtered, CancellationToken ct)
    {
        // Attribute each matched book to its primary author — the lowest-Order
        // WorkAuthor of the first Work (by Work.Id) — to avoid the M-author
        // fan-out on a search hitting a compendium.
        var rawAuthors = await filtered
            .SelectMany(b => b.Works.SelectMany(w => w.WorkAuthors.Select(wa => new
            {
                BookId = b.Id,
                CanonicalId = wa.Author.CanonicalAuthorId ?? wa.AuthorId,
                WorkId = w.Id,
                wa.Order,
            })))
            .ToListAsync(ct);

        // Pick the primary (smallest WorkId, then smallest Order) per book
        // in-memory. Bounded by matched-books × authors-per-book; fine since
        // search narrows the set first.
        var raw = rawAuthors
            .GroupBy(x => x.BookId)
            .Select(g => g.OrderBy(x => x.WorkId).ThenBy(x => x.Order).First())
            .GroupBy(x => x.CanonicalId)
            .Select(g => new { CanonicalId = g.Key, Count = g.Count() })
            .ToList();

        var names = await AuthorNamesAsync(db, raw.Select(r => r.CanonicalId), ct);
        return ToGroupRows(raw.Select(r => (r.CanonicalId, r.Count)), names);
    }

    private static async Task<List<GroupRow>> GroupByGenreAsync(
        BookTrackerDbContext db, IQueryable<Book> filtered, CancellationToken ct)
    {
        var raw = await filtered
            .SelectMany(b => b.Works.SelectMany(w => w.Genres.Select(g => new { BookId = b.Id, GenreId = g.Id })))
            .Distinct()
            .GroupBy(x => x.GenreId)
            .Select(g => new { GenreId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var genreIds = raw.Select(r => r.GenreId).ToList();
        var names = await db.Genres
            .Where(g => genreIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var groups = ToGroupRows(raw.Select(r => (r.GenreId, r.Count)), names);

        var ungenredCount = await filtered.CountAsync(b => !b.Works.Any(w => w.Genres.Any()), ct);
        if (ungenredCount > 0)
            groups.Add(new GroupRow(GroupRow.NoneKey, "(no genre)", ungenredCount));

        return groups;
    }

    // TD-6(b): the three "id → name lookup, build GroupRow ordered by Label" tails
    // were copy-pasted across the grouping paths; collapsed into one helper.
    private static List<GroupRow> ToGroupRows(
        IEnumerable<(int Key, int Count)> rawCounts, IReadOnlyDictionary<int, string> names)
        => rawCounts
            .Select(r => new GroupRow(
                r.Key.ToString(),
                names.GetValueOrDefault(r.Key) ?? "(unknown)",
                r.Count))
            .OrderBy(g => g.Label)
            .ToList();

    private static async Task<Dictionary<int, string>> AuthorNamesAsync(
        BookTrackerDbContext db, IEnumerable<int> canonicalIds, CancellationToken ct)
    {
        var ids = canonicalIds.ToList();
        return await db.Authors
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    }
}
