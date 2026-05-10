using BookTracker.Data;
using BookTracker.Shared.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services.Catalog;

// Slim catalog projection consumed by the bookshop-mode offline cache
// (see docs/bookshop-mode-design.md) and by the future BookTracker.Mobile
// MAUI app (see docs/mobile-app-design.md). Returns just enough data
// for the killer mobile use cases — ISBN-have-I-got-this and
// author-lookup — without the bandwidth or PII of a full edit-flow
// payload.
//
// Size budget at the 3000+ books target: ~480KB raw / ~150KB gzipped.
// Single endpoint hit; SW pre-caches; IndexedDB stores client-side.
//
// Deliberately omits cover URLs, notes, tags, edition/copy detail.
// Bookshop mode is read-only-lookup; full detail is reachable via
// "Open in app" deep-links to /books/{id} (online only).
//
// DTO records live in BookTracker.Shared.Catalog — no EF dependency
// there so the mobile project can reference the contract cleanly.
public interface ICatalogSnapshotService
{
    Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public class CatalogSnapshotService(
    IDbContextFactory<BookTrackerDbContext> dbFactory) : ICatalogSnapshotService
{
    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Books — pull a flat shape and project in-memory. Bounded by total
        // catalog size (3000+ target), so the materialise-then-project cost
        // is fine. AsNoTracking per scale-audit defaults; this is a read-
        // only path with immediate projection into records.
        var booksRaw = await db.Books
            .AsNoTracking()
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.Status,
                b.Rating,
                Authors = b.Works.SelectMany(w => w.WorkAuthors.Select(wa => new
                {
                    wa.Author.Name,
                    WorkId = w.Id,
                    wa.Order,
                })).ToList(),
                Isbns = b.Editions
                    .Where(e => e.Isbn != null && e.Isbn != "")
                    .Select(e => e.Isbn!)
                    .ToList(),
                // Series membership lives on Work. For multi-Work
                // compendiums take the first Work by Work.Id —
                // matches the PrimaryAuthor convention. Single-Work
                // books are unambiguous.
                FirstWorkSeries = b.Works
                    .OrderBy(w => w.Id)
                    .Select(w => new { w.SeriesId, w.SeriesOrder })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var books = booksRaw
            .Select(b => new BookSnapshot(
                b.Id,
                b.Title,
                // Primary author = lowest-Order WorkAuthor of the first
                // Work (by Work.Id). Single-Work books are unambiguous;
                // compendiums get the primary of whichever Work sorts
                // first. Same shape as the library-grouping-respects-
                // author-filter fix uses.
                b.Authors.OrderBy(a => a.WorkId).ThenBy(a => a.Order).Select(a => a.Name).FirstOrDefault() ?? "(unknown)",
                // All credited authors, in (Work.Id, Order) sequence,
                // distinct by name. Renders as the result-card subtitle
                // when the book is shown in bookshop mode.
                b.Authors.OrderBy(a => a.WorkId).ThenBy(a => a.Order).Select(a => a.Name).Distinct().ToList(),
                b.Status.ToString(),
                b.Rating,
                b.Isbns.Distinct().ToList(),
                b.FirstWorkSeries?.SeriesId,
                b.FirstWorkSeries?.SeriesOrder))
            .OrderBy(b => b.Title)
            .ToList();

        // Authors — direct counts per row. For canonicals, the rolled-up
        // count (canonical + all aliases' books) needs a separate query
        // to avoid double-counting books that are credited to BOTH a
        // canonical and its alias.
        var directCounts = await db.Authors
            .AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.Name,
                CanonicalId = a.CanonicalAuthorId ?? a.Id,
                DirectBookCount = a.Works.SelectMany(w => w.Books).Select(b => b.Id).Distinct().Count(),
            })
            .ToListAsync(ct);

        // Distinct (canonical_id, book_id) pairs across the whole
        // WorkAuthor graph. Group-by canonical gives the rolled-up
        // count without double-counting books credited to both
        // canonical and alias.
        var canonicalBookPairs = await db.WorkAuthors
            .AsNoTracking()
            .SelectMany(wa => wa.Work.Books.Select(b => new
            {
                CanonicalId = wa.Author.CanonicalAuthorId ?? wa.AuthorId,
                BookId = b.Id,
            }))
            .Distinct()
            .ToListAsync(ct);

        var canonicalCounts = canonicalBookPairs
            .GroupBy(x => x.CanonicalId)
            .ToDictionary(g => g.Key, g => g.Count());

        var authors = directCounts
            .Select(a => new AuthorSnapshot(
                a.Id,
                a.Name,
                a.CanonicalId,
                // Canonical row → use the rolled-up count.
                // Alias row → use its own direct count (so tapping
                // "Richard Bachman" shows just Bachman's titles, while
                // tapping "Stephen King" shows the rolled-up total).
                BookCount: a.Id == a.CanonicalId
                    ? canonicalCounts.GetValueOrDefault(a.CanonicalId, 0)
                    : a.DirectBookCount))
            .OrderBy(a => a.Name)
            .ToList();

        // Series — only those referenced by at least one book in the
        // snapshot. Derive the IDs from booksRaw (already materialised)
        // rather than re-querying through Series→Works→Books — that
        // 3-level nav-property chain doesn't translate cleanly under
        // the M:N BookWork join table on EF Core 10.x. Side effect of
        // the simpler shape: a series only attached via a non-first
        // Work of a compendium won't surface here, which is fine for
        // v1; the bookshop "missing books" view operates against books
        // with a clear primary series anyway.
        var referencedSeriesIds = booksRaw
            .Select(b => b.FirstWorkSeries?.SeriesId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var seriesRaw = await db.Series
            .AsNoTracking()
            .Where(s => referencedSeriesIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Type, s.ExpectedCount })
            .ToListAsync(ct);

        var series = seriesRaw
            .Select(s => new SeriesSnapshot(s.Id, s.Name, s.Type.ToString(), s.ExpectedCount))
            .OrderBy(s => s.Name)
            .ToList();

        // Version is the deployed commit SHA when available (so the
        // client-side cache can detect a deploy and trigger a refresh)
        // or "dev" when running locally without the build-time SHA
        // injection. SyncedAt is server clock at projection time.
        return new CatalogSnapshot(
            BuildInfo.ShortSha ?? "dev",
            DateTime.UtcNow,
            books,
            authors,
            series);
    }
}
