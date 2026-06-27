using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Single SQL-side owner of the per-author rollup counts (distinct works, books,
// series). Before the back-end arc close-out, /authors (GetAuthorList), the
// mobile catalog snapshot (GetCatalogSnapshot), and Home's top-authors tally
// each computed an author's counts a different way — drift-prone with no
// compiler signal (the old TD-17 b/c). Consolidated here so every consumer reads
// one definition.
//
// All counts use Author-role authorship only — Editor/Translator/Illustrator are
// excluded from "books by X" so reference-work contributors don't pollute the
// list. The per-author dedup — the expensive part — is pushed to SQL (a
// `SELECT DISTINCT` over the authorship join); the distinct pairs come back and
// the final group-count is a trivial in-memory tally bounded by the catalogue
// size (EF can't translate `Distinct().GroupBy()` to a single SQL statement).
//
// Canonical rollup is a plain in-memory SUM of the member authors' counts (own +
// aliases) — see RollUpToCanonical. A title credited to BOTH a canonical author
// AND one of its aliases is counted once per crediting member rather than once
// overall; that's a deliberate accepted imprecision (one book across ~2000
// today — King's Bachman omnibus), traded for dropping a cross-member DISTINCT
// that bought numeric perfection nobody can see.
public static class AuthorRollups
{
    public record AuthorCounts(int WorkCount, int BookCount, int SeriesCount);

    // EF projection target — named (not anonymous) so the grouped-count helper
    // can take it as IQueryable<KeyVal>. EF translates `new KeyVal(a, b)` to a
    // two-column projection and `.Distinct()` to SQL DISTINCT over those columns.
    public sealed record KeyVal(int Key, int Val);

    // Distinct work/book/series counts keyed by individual author id (Author-role
    // only). The single SQL touch-point; canonical totals are summed from this.
    public static async Task<Dictionary<int, AuthorCounts>> PerAuthorAsync(
        BookTrackerDbContext db, CancellationToken ct)
    {
        var baseQ = db.WorkAuthors.AsNoTracking().Where(wa => wa.Role == AuthorRole.Author);
        var works = await CountByKeyAsync(
            baseQ.Select(wa => new KeyVal(wa.AuthorId, wa.WorkId)), ct);
        var books = await CountByKeyAsync(
            baseQ.SelectMany(wa => wa.Work.Books.Select(b => new KeyVal(wa.AuthorId, b.Id))), ct);
        var series = await CountByKeyAsync(
            baseQ.Where(wa => wa.Work.SeriesId != null)
                 .Select(wa => new KeyVal(wa.AuthorId, wa.Work.SeriesId!.Value)), ct);
        return Merge(works, books, series);
    }

    // Just the distinct book count per author (Author-role) — for consumers that
    // need only BookCount (Home top-authors, the catalog snapshot) and shouldn't
    // pay for the works + series distinct scans PerAuthorAsync also runs (F3).
    public static Task<Dictionary<int, int>> PerAuthorBookCountAsync(
        BookTrackerDbContext db, CancellationToken ct)
    {
        var baseQ = db.WorkAuthors.AsNoTracking().Where(wa => wa.Role == AuthorRole.Author);
        return CountByKeyAsync(
            baseQ.SelectMany(wa => wa.Work.Books.Select(b => new KeyVal(wa.AuthorId, b.Id))), ct);
    }

    // Roll per-author counts up onto canonical authors by summing each canonical's
    // members. `membership` maps every author id to its canonical id (own id when
    // not an alias) — callers build it from data they already load. Result is
    // keyed by canonical id; standalone authors map to themselves. See the class
    // note on the accepted cross-member double-count.
    public static Dictionary<int, AuthorCounts> RollUpToCanonical(
        IReadOnlyDictionary<int, AuthorCounts> perAuthor,
        IEnumerable<(int AuthorId, int CanonicalId)> membership)
    {
        var result = new Dictionary<int, AuthorCounts>();
        foreach (var (authorId, canonicalId) in membership)
        {
            if (perAuthor.GetValueOrDefault(authorId) is not { } c) continue;
            var cur = result.GetValueOrDefault(canonicalId, new AuthorCounts(0, 0, 0));
            result[canonicalId] = new AuthorCounts(
                cur.WorkCount + c.WorkCount,
                cur.BookCount + c.BookCount,
                cur.SeriesCount + c.SeriesCount);
        }
        return result;
    }

    // Int overload — sum a single per-author scalar (e.g. BookCount) up onto
    // canonicals. Same membership + accepted cross-member double-count as above.
    public static Dictionary<int, int> RollUpToCanonical(
        IReadOnlyDictionary<int, int> perAuthor,
        IEnumerable<(int AuthorId, int CanonicalId)> membership)
    {
        var result = new Dictionary<int, int>();
        foreach (var (authorId, canonicalId) in membership)
        {
            if (!perAuthor.TryGetValue(authorId, out var v)) continue;
            result[canonicalId] = result.GetValueOrDefault(canonicalId) + v;
        }
        return result;
    }

    // DISTINCT the (key, value) pairs server-side, then group-count in memory.
    // EF can't translate `Distinct().GroupBy(...)` to SQL (it walls on the
    // grouped subquery — the same limitation the old in-app rollups dodged), but
    // it DOES translate `Select(pair).Distinct()` to a `SELECT DISTINCT` over the
    // join (GetCatalogSnapshot's proven shape). So the dedup — the heavy part —
    // runs in SQL; only the distinct pairs cross the wire and the final count is
    // a trivial in-memory tally bounded by the catalogue size.
    static async Task<Dictionary<int, int>> CountByKeyAsync(IQueryable<KeyVal> pairs, CancellationToken ct)
    {
        var distinct = await pairs.Distinct().ToListAsync(ct);
        return distinct
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    static Dictionary<int, AuthorCounts> Merge(
        Dictionary<int, int> works, Dictionary<int, int> books, Dictionary<int, int> series)
    {
        // Work-count keys are the superset (an author with no Author-role works
        // produces no rows in any grouping and correctly has no entry → callers
        // default to 0).
        return works.Keys.ToDictionary(
            id => id,
            id => new AuthorCounts(
                works.GetValueOrDefault(id),
                books.GetValueOrDefault(id),
                series.GetValueOrDefault(id)));
    }
}
