using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Single SQL-side owner of the per-author rollup counts (distinct works, books,
// series). Before the back-end arc close-out, /authors (GetAuthorList), the
// mobile catalog snapshot (GetCatalogSnapshot), and Home's top-authors tally
// each computed an author's counts a different way — the canonical book rollup
// was *documented to match* between /authors and the snapshot but was computed
// two ways (in-app HashSet vs DB Distinct pairs), drift-prone with no compiler
// signal (the old TD-17 b/c). Consolidated here so every consumer reads one
// definition.
//
// All counts use Author-role authorship only — Editor/Translator/Illustrator are
// excluded from "books by X" so reference-work contributors don't pollute the
// list. The dedup — the expensive part — is pushed to SQL (a `SELECT DISTINCT`
// over the authorship join); the distinct pairs come back and the final
// group-count is a trivial in-memory tally bounded by the catalogue size (EF
// can't translate `Distinct().GroupBy()` to a single SQL statement). Two
// groupings:
//   ByCanonicalAsync — own id + every alias rolled up (canonical author rows).
//                      The DISTINCT runs at the canonical key so a book credited
//                      to BOTH a canonical and its alias counts once.
//   ByAuthorAsync    — each author's own links only (alias rows show own counts).
public static class AuthorRollups
{
    public record AuthorCounts(int WorkCount, int BookCount, int SeriesCount);

    // EF projection target — named (not anonymous) so the grouped-count helper
    // can take it as IQueryable<KeyVal>. EF translates `new KeyVal(a, b)` to a
    // two-column projection and `.Distinct()` to SQL DISTINCT over those columns.
    public sealed record KeyVal(int Key, int Val);

    // Counts rolled up onto the canonical author (CanonicalAuthorId ?? AuthorId).
    public static async Task<Dictionary<int, AuthorCounts>> ByCanonicalAsync(
        BookTrackerDbContext db, CancellationToken ct)
    {
        var baseQ = db.WorkAuthors.AsNoTracking().Where(wa => wa.Role == AuthorRole.Author);
        var works = await CountByKeyAsync(
            baseQ.Select(wa => new KeyVal(wa.Author.CanonicalAuthorId ?? wa.AuthorId, wa.WorkId)), ct);
        var books = await CountByKeyAsync(
            baseQ.SelectMany(wa => wa.Work.Books.Select(b => new KeyVal(wa.Author.CanonicalAuthorId ?? wa.AuthorId, b.Id))), ct);
        var series = await CountByKeyAsync(
            baseQ.Where(wa => wa.Work.SeriesId != null)
                 .Select(wa => new KeyVal(wa.Author.CanonicalAuthorId ?? wa.AuthorId, wa.Work.SeriesId!.Value)), ct);
        return Merge(works, books, series);
    }

    // Counts per individual author id (no alias rollup) — for alias rows, which
    // show only their own titles.
    public static async Task<Dictionary<int, AuthorCounts>> ByAuthorAsync(
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

    // DISTINCT the (key, value) pairs server-side, then group-count in memory.
    // EF can't translate `Distinct().GroupBy(...)` to SQL (it walls on the
    // grouped subquery — the same limitation the old in-app rollups dodged), but
    // it DOES translate `Select(pair).Distinct()` to a `SELECT DISTINCT` over the
    // join (this is GetCatalogSnapshot's proven shape). So the dedup — the heavy
    // part — runs in SQL; only the distinct pairs cross the wire and the final
    // count is a trivial in-memory tally bounded by the catalogue size.
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
        // Work-count keys are the superset (an author with no works produces no
        // rows in any grouping and correctly has no entry → callers default to 0).
        return works.Keys
            .Union(books.Keys).Union(series.Keys)
            .ToDictionary(
                id => id,
                id => new AuthorCounts(
                    works.GetValueOrDefault(id),
                    books.GetValueOrDefault(id),
                    series.GetValueOrDefault(id)));
    }
}
