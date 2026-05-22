using BookTracker.Data;
using BookTracker.Data.Models;
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
// Includes Book.DefaultCoverArtUrl for Bookshelf cover display
// (downloaded once + cached locally as a 200px JPEG). Omits notes,
// tags, edition/copy detail — those are edit-flow concerns reached
// via "Open in app" deep-links to /books/{id} (online only).
// Bookcase's /bookshop ignores the CoverUrl field (it deep-links
// for visuals).
//
// DTO records live in BookTracker.Shared.Catalog — no EF dependency
// there so the mobile project can reference the contract cleanly.
public interface ICatalogSnapshotService
{
    /// <summary>Full snapshot when <paramref name="since"/> is null;
    /// delta-of-Books-changed-after-since otherwise. Authors + Series
    /// are always full-listed regardless of <paramref name="since"/>
    /// (they're tiny and the client needs the full set anyway). The
    /// returned <see cref="CatalogSnapshot.LatestUpdatedAt"/> is the
    /// max Book.UpdatedAt across the response — clients store it and
    /// send it as <c>?since=</c> on the next call.</summary>
    Task<CatalogSnapshot> GetSnapshotAsync(DateTime? since = null, CancellationToken ct = default);
}

public class CatalogSnapshotService(
    IDbContextFactory<BookTrackerDbContext> dbFactory) : ICatalogSnapshotService
{
    public async Task<CatalogSnapshot> GetSnapshotAsync(DateTime? since = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Books — pull a flat shape and project in-memory. Bounded by total
        // catalog size (3000+ target), so the materialise-then-project cost
        // is fine. AsNoTracking per scale-audit defaults; this is a read-
        // only path with immediate projection into records.
        //
        // When `since` is non-null, the IX_Books_UpdatedAt index makes the
        // filter a B-tree seek — at delta-typical sizes (a handful of
        // changed Books) this returns sub-millisecond.
        var booksQuery = db.Books.AsNoTracking();
        if (since is DateTime sinceUtc)
        {
            booksQuery = booksQuery.Where(b => b.UpdatedAt > sinceUtc);
        }

        var booksRaw = await booksQuery
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.Status,
                b.Rating,
                b.DefaultCoverArtUrl,
                b.UpdatedAt,
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
                // Per-Edition detail for the Bookshelf enhanced-card
                // view. Includes the no-ISBN editions (pre-1974) too —
                // those still have a Format + maybe a CoverUrl worth
                // showing. Distinct from the Isbns flat list above
                // which filters them out.
                Editions = b.Editions
                    .Select(e => new
                    {
                        e.Id,
                        e.Isbn,
                        e.Format,
                        e.CoverUrl,
                    })
                    .ToList(),
                // Per-Work detail for compendiums. PrimaryAuthor per
                // Work picks the lowest-Order WorkAuthor on THAT work
                // (not the Book-level rollup) so the compendium row
                // shows each story's true attribution. Single-Work
                // books still ship a one-element list; the client
                // hides the section when Count <= 1.
                Works = b.Works
                    .OrderBy(w => w.Id)
                    .Select(w => new
                    {
                        w.Id,
                        w.Title,
                        WorkAuthors = w.WorkAuthors
                            .OrderBy(wa => wa.Order)
                            .Select(wa => wa.Author.Name)
                            .ToList(),
                    })
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
                b.FirstWorkSeries?.SeriesOrder,
                b.DefaultCoverArtUrl,
                Editions: b.Editions
                    .Select(e => new EditionSnapshot(e.Id, e.Isbn, e.Format.ToString(), e.CoverUrl))
                    .ToList(),
                Works: b.Works
                    .Select(w => new WorkSnapshot(
                        w.Id,
                        w.Title,
                        w.WorkAuthors.FirstOrDefault() ?? "(unknown)"))
                    .ToList()))
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
        // canonical and alias. Filtered to Role = Author so the snapshot's
        // BookCount stays a true "books this person wrote" rollup —
        // matches the /authors and Home top-authors semantics.
        var canonicalBookPairs = await db.WorkAuthors
            .AsNoTracking()
            .Where(wa => wa.Role == AuthorRole.Author)
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

        // Series — always full-listed (small set, ~tens at most).
        // Earlier versions filtered to only those referenced by Books
        // in the snapshot to shave a few KB, but that broke the
        // delta-sync rename case: when only one Book changes, a Series
        // rename on that Book's series surfaces, but Series renames
        // that don't bump any owning Books wouldn't propagate to
        // clients on delta refreshes. Always-all is correct + simpler.
        var seriesRaw = await db.Series
            .AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Type, s.ExpectedCount })
            .ToListAsync(ct);

        var series = seriesRaw
            .Select(s => new SeriesSnapshot(s.Id, s.Name, s.Type.ToString(), s.ExpectedCount))
            .OrderBy(s => s.Name)
            .ToList();

        // Tombstones — soft-deleted Books since the `since` token.
        // Only emitted on delta calls; a full snapshot returns an
        // empty list (the client is doing a fresh load and has no
        // local rows to drop).
        //
        // IgnoreQueryFilters() opts out of the global DeletedAt == null
        // filter so dead husks are visible to this query. We project
        // both Id and DeletedAt — the Id goes on the wire, the
        // DeletedAt feeds into LatestUpdatedAt so the next-token
        // calculation advances past a pure-tombstone delta. The
        // IX_Books_DeletedAt filtered index covers the predicate
        // cheaply (most rows have DeletedAt = NULL and are skipped).
        var tombstones = since is DateTime sinceForTombstones
            ? await db.Books
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(b => b.DeletedAt != null && b.DeletedAt > sinceForTombstones)
                .Select(b => new { b.Id, b.DeletedAt })
                .ToListAsync(ct)
            : [];

        var deletedIds = tombstones.Select(t => t.Id).ToList();

        // LatestUpdatedAt = max stamp across the rows that contributed
        // to this response — Book.UpdatedAt for live rows, Book.DeletedAt
        // for tombstones. Falling back to `since` (or now) when the
        // delta is fully empty is fine; the client just sends the same
        // token and gets another empty response next time. Falling back
        // when the delta has tombstones would cause an infinite re-fetch
        // loop (client keeps sending the same `since`, server keeps
        // returning the same dead IDs).
        var syncedAt = DateTime.UtcNow;
        DateTime? maxBookStamp = booksRaw.Count > 0 ? booksRaw.Max(b => b.UpdatedAt) : null;
        DateTime? maxTombstoneStamp = tombstones.Count > 0
            ? tombstones.Max(t => t.DeletedAt!.Value)
            : null;
        var latestUpdatedAt = (maxBookStamp, maxTombstoneStamp) switch
        {
            (DateTime b, DateTime t) => b > t ? b : t,
            (DateTime b, null) => b,
            (null, DateTime t) => t,
            _ => since ?? syncedAt,
        };

        // Version is the deployed commit SHA when available (so the
        // client-side cache can detect a deploy and trigger a refresh)
        // or "dev" when running locally without the build-time SHA
        // injection. SyncedAt is server clock at projection time.
        return new CatalogSnapshot(
            BuildInfo.ShortSha ?? "dev",
            syncedAt,
            books,
            authors,
            series,
            latestUpdatedAt,
            deletedIds);
    }
}
