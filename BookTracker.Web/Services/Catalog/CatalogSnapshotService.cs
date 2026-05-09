using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services.Catalog;

// Slim catalog projection consumed by the bookshop-mode offline cache
// (see docs/bookshop-mode-design.md). Returns just enough data for the
// killer mobile use cases — ISBN-have-I-got-this and author-lookup —
// without the bandwidth or PII of a full edit-flow payload.
//
// Size budget at the 3000+ books target: ~480KB raw / ~150KB gzipped.
// Single endpoint hit; SW pre-caches; IndexedDB stores client-side.
//
// Deliberately omits cover URLs, notes, tags, edition/copy detail.
// Bookshop mode is read-only-lookup; full detail is reachable via
// "Open in app" deep-links to /books/{id} (online only).
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
                b.Status,
                b.Rating,
                b.Isbns.Distinct().ToList()))
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

        // Version is the deployed commit SHA when available (so the
        // client-side cache can detect a deploy and trigger a refresh)
        // or "dev" when running locally without the build-time SHA
        // injection. SyncedAt is server clock at projection time.
        return new CatalogSnapshot(
            BuildInfo.ShortSha ?? "dev",
            DateTime.UtcNow,
            books,
            authors);
    }
}

public record CatalogSnapshot(
    string Version,
    DateTime SyncedAt,
    IReadOnlyList<BookSnapshot> Books,
    IReadOnlyList<AuthorSnapshot> Authors);

public record BookSnapshot(
    int Id,
    string Title,
    string PrimaryAuthor,
    IReadOnlyList<string> AllAuthors,
    BookStatus Status,
    int Rating,
    IReadOnlyList<string> Isbns);

public record AuthorSnapshot(
    int Id,
    string Name,
    int CanonicalId,
    int BookCount);
