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
// list. Each count is a correlated `COUNT(DISTINCT ...)` subquery over the
// author's Author-role links, computed in SQL — one row per author comes back,
// no per-link payload (EF can't fold `Distinct().GroupBy()` / `GROUP BY
// COUNT(DISTINCT)` over the join into one statement, but it DOES translate the
// per-author correlated subquery — the shape the old snapshot directCounts used).
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

    // Distinct work/book/series counts keyed by individual author id (Author-role
    // only). Canonical totals are summed from this.
    public static async Task<Dictionary<int, AuthorCounts>> PerAuthorAsync(
        BookTrackerDbContext db, CancellationToken ct)
    {
        var rows = await db.Authors
            .AsNoTracking()
            .Select(a => new
            {
                a.Id,
                Works = a.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author)
                    .Select(wa => wa.WorkId).Distinct().Count(),
                Books = a.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author)
                    .SelectMany(wa => wa.Work.Books).Select(b => b.Id).Distinct().Count(),
                Series = a.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author && wa.Work.SeriesId != null)
                    .Select(wa => wa.Work.SeriesId!.Value).Distinct().Count(),
            })
            .ToListAsync(ct);

        // Authors with no Author-role works are dropped (no entry → callers
        // default to 0), matching the old grouping which produced no key for them.
        return rows
            .Where(x => x.Works > 0)
            .ToDictionary(x => x.Id, x => new AuthorCounts(x.Works, x.Books, x.Series));
    }

    // Just the distinct book count per author (Author-role) — for consumers that
    // need only BookCount (Home top-authors, the catalog snapshot) and shouldn't
    // pay for the works + series counts (F3). The "Author-role distinct books"
    // definition is kept identical to PerAuthorAsync's Books above so /authors and
    // the snapshot can't drift (TD-17 b: their BookCounts are documented to match).
    public static async Task<Dictionary<int, int>> PerAuthorBookCountAsync(
        BookTrackerDbContext db, CancellationToken ct)
    {
        var rows = await db.Authors
            .AsNoTracking()
            .Select(a => new
            {
                a.Id,
                Books = a.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author)
                    .SelectMany(wa => wa.Work.Books).Select(b => b.Id).Distinct().Count(),
            })
            .ToListAsync(ct);

        return rows
            .Where(x => x.Books > 0)
            .ToDictionary(x => x.Id, x => x.Books);
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

    // The single canonical-vs-alias display rule shared by /authors and the
    // snapshot: a canonical row (authorId == its own canonical id) shows the
    // rolled-up total (own + aliases) from `byCanonical`; an alias row shows just
    // its own per-author value from `perAuthor`. Missing keys default (0 / null)
    // so works-less authors read as zero. Generic over the value type — int for
    // the snapshot's BookCount, AuthorCounts for the /authors triple.
    public static TVal? SelectForDisplay<TVal>(
        int authorId,
        int canonicalId,
        IReadOnlyDictionary<int, TVal> byCanonical,
        IReadOnlyDictionary<int, TVal> perAuthor)
        => authorId == canonicalId
            ? byCanonical.GetValueOrDefault(authorId)
            : perAuthor.GetValueOrDefault(authorId);
}
