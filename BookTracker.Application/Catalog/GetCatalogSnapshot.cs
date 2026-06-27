using BookTracker.Application.Authors;
using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Shared.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Catalog;

// Slim catalog projection consumed by the bookshop-mode offline cache
// (see docs/bookshop-mode-design.md) and the BookTracker.Mobile MAUI app.
// Returns just enough for the killer mobile use cases — ISBN-have-I-got-this
// and author-lookup — without the bandwidth or PII of a full edit-flow
// payload.
//
// Size budget at the 3000+ books target: ~480KB raw / ~150KB gzipped.
// DTO records live in BookTracker.Shared.Catalog — no EF dependency there so
// the mobile project can reference the contract cleanly.
//
// Relocated from BookTracker.Web.Services.Catalog in PR6 — the canonical
// read-model template (C5). `Version` is the deployed commit SHA, supplied by
// the host endpoint (BuildInfo stays a host concern) so this handler stays
// host-agnostic.
//
/// <summary>Full snapshot when <see cref="Since"/> is null; delta-of-Books-
/// changed-after-since otherwise. Authors + Series are always full-listed
/// regardless of <see cref="Since"/> (they're tiny and the client needs the
/// full set anyway). The returned <see cref="CatalogSnapshot.LatestUpdatedAt"/>
/// is the max Book.UpdatedAt across the response — clients store it and send it
/// as <c>?since=</c> on the next call.</summary>
public sealed record GetCatalogSnapshot(DateTime? Since, string Version) : IQuery<CatalogSnapshot>;

public sealed class GetCatalogSnapshotHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetCatalogSnapshot, CatalogSnapshot>
{
    public async Task<CatalogSnapshot> HandleAsync(GetCatalogSnapshot query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var since = query.Since;

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
                    wa.Role,
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
                        e.EditionNumber,
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
                        // Every contributor on this Work with their role.
                        // Server emits the full list; per-Work PrimaryAuthor
                        // is computed downstream by filtering Role=Author.
                        WorkContributors = w.WorkAuthors
                            .OrderBy(wa => wa.Role == AuthorRole.Author ? 0 : 1)
                            .ThenBy(wa => (int)wa.Role)
                            .ThenBy(wa => wa.Order)
                            .Select(wa => new { wa.Author.Name, wa.Role })
                            .ToList(),
                    })
                    .ToList(),
                // Series membership lives on Work. For multi-Work
                // compendiums take the first Work by Work.Id —
                // matches the PrimaryAuthor convention. Single-Work
                // books are unambiguous.
                FirstWorkSeries = b.Works
                    .OrderBy(w => w.Id)
                    .Select(w => new { w.SeriesId, w.SeriesOrder, w.SeriesOrderDisplay })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var books = booksRaw
            .Select(b => new BookSnapshot(
                b.Id,
                b.Title,
                // Primary author = lowest-Order Author-role WorkAuthor of
                // the first Work (by Work.Id). Single-Work books are
                // unambiguous; compendiums get the primary of whichever
                // Work sorts first. For editor-only Works (dictionaries,
                // anthologies) with no Author-role contributor, falls
                // back to the lowest-Order non-Author with role suffix
                // — e.g. "Doug Mauss (editor)" — via DisplayPrimary.
                WorkAuthorshipFormatter.DisplayPrimary(b.Authors
                    .OrderBy(a => a.WorkId)
                    .ThenBy(a => a.Role == AuthorRole.Author ? 0 : 1)
                    .ThenBy(a => (int)a.Role)
                    .ThenBy(a => a.Order)
                    .Select(a => (a.Name, a.Role))),
                // All credited contributors with their role, in (Work.Id,
                // Role, Order) sequence — Author-role first, then other
                // roles in enum order. Distinct by (Name, Role) so a
                // single person credited as both Author and Illustrator on
                // the same Book shows up once per role.
                b.Authors
                    .OrderBy(a => a.WorkId)
                    .ThenBy(a => a.Role == AuthorRole.Author ? 0 : 1)
                    .ThenBy(a => (int)a.Role)
                    .ThenBy(a => a.Order)
                    .Select(a => new AuthorContribution(a.Name, a.Role.ToString()))
                    .DistinctBy(c => (c.Name, c.Role))
                    .ToList(),
                b.Status.ToString(),
                b.Rating,
                b.Isbns.Distinct().ToList(),
                b.FirstWorkSeries?.SeriesId,
                // Real (floored) integer order. Mobile now carries
                // SeriesOrderDisplay and applies the same "display-only orders
                // don't claim a numbered slot" guard as the web gap detection,
                // so the floored int is safe to ship.
                b.FirstWorkSeries?.SeriesOrder,
                b.DefaultCoverArtUrl,
                Editions: b.Editions
                    .Select(e => new EditionSnapshot(e.Id, e.Isbn, e.Format.ToString(), e.CoverUrl, e.EditionNumber))
                    .ToList(),
                Works: b.Works
                    .Select(w => new WorkSnapshot(
                        w.Id,
                        w.Title,
                        // Per-Work PrimaryAuthor = first Author-role
                        // contributor on this Work, or for editor-only
                        // Works the first non-Author contributor with
                        // role suffix (e.g. "Doug Mauss (editor)"). The
                        // pre-projection already sorted Author-role first
                        // then by (Role, Order), so DisplayPrimary just
                        // picks the head of the list.
                        WorkAuthorshipFormatter.DisplayPrimary(
                            w.WorkContributors.Select(c => (c.Name, c.Role))),
                        // Full contributor list with role per entry.
                        Contributors: w.WorkContributors
                            .Select(c => new AuthorContribution(c.Name, c.Role.ToString()))
                            .ToList()))
                    .ToList(),
                SeriesOrderDisplay: b.FirstWorkSeries?.SeriesOrderDisplay))
            .OrderBy(b => b.Title)
            .ToList();

        // Authors — BookCount per row via the shared SQL-side rollup (Author-role
        // distinct books), so /authors, Home, and this snapshot read one
        // definition. Canonical rows take the rolled-up count (own + aliases,
        // de-duped at the canonical key so a book credited to both counts once);
        // alias rows take their own (tapping "Richard Bachman" shows just
        // Bachman's titles, "Stephen King" the rolled-up total).
        var authorRows = await db.Authors
            .AsNoTracking()
            .Select(a => new { a.Id, a.Name, CanonicalId = a.CanonicalAuthorId ?? a.Id })
            .ToListAsync(ct);

        var perAuthorBooks = await AuthorRollups.PerAuthorBookCountAsync(db, ct);
        var byCanonical = AuthorRollups.RollUpToCanonical(
            perAuthorBooks, authorRows.Select(a => (a.Id, a.CanonicalId)));

        var authors = authorRows
            .Select(a => new AuthorSnapshot(
                a.Id,
                a.Name,
                a.CanonicalId,
                BookCount: a.Id == a.CanonicalId
                    ? byCanonical.GetValueOrDefault(a.CanonicalId)
                    : perAuthorBooks.GetValueOrDefault(a.Id)))
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

        // Version is the deployed commit SHA supplied by the host (so the
        // client-side cache can detect a deploy and trigger a refresh).
        // SyncedAt is server clock at projection time.
        return new CatalogSnapshot(
            query.Version,
            syncedAt,
            books,
            authors,
            series,
            latestUpdatedAt,
            deletedIds);
    }
}
