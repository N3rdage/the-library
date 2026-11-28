using BookTracker.Application.Books;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Duplicates;

/// <summary>Scans the library for candidate duplicate pairs across Authors,
/// Works, Books and Editions, attaching any recorded dismissal to each pair.
/// Read-only: orphaned dismissals are removed by <see cref="SweepOrphanedDismissals"/>,
/// which the page dispatches before this query.</summary>
public sealed record GetDuplicateReport : IQuery<DuplicateReport>;

public sealed class GetDuplicateReportHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetDuplicateReport, DuplicateReport>
{
    public async Task<DuplicateReport> HandleAsync(GetDuplicateReport query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var ignoredLookup = await BuildIgnoredLookupAsync(db, ct);

        return new DuplicateReport(
            Authors: await DetectAuthorsAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Author), ct),
            Works: await DetectWorksAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Work), ct),
            Books: await DetectBooksAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Book), ct),
            Editions: await DetectEditionsAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Edition), ct));
    }

    private static async Task<Dictionary<DuplicateEntityType, Dictionary<(int Lower, int Higher), DismissalInfo>>> BuildIgnoredLookupAsync(
        BookTrackerDbContext db, CancellationToken ct)
    {
        var all = await db.IgnoredDuplicates
            .Select(i => new IgnoredPairProjection(i.Id, i.EntityType, i.LowerId, i.HigherId, i.DismissedAt, i.Note))
            .ToListAsync(ct);
        return all
            .GroupBy(i => i.EntityType)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(i => (i.LowerId, i.HigherId), i => new DismissalInfo(i.Id, i.DismissedAt, i.Note)));
    }

    private static DismissalInfo? LookupDismissal(
        Dictionary<(int Lower, int Higher), DismissalInfo>? ignored,
        int lower, int higher)
    {
        if (ignored is null) return null;
        return ignored.TryGetValue((lower, higher), out var dismissal) ? dismissal : null;
    }

    private sealed record IgnoredPairProjection(
        int Id,
        DuplicateEntityType EntityType,
        int LowerId,
        int HigherId,
        DateTime DismissedAt,
        string? Note);

    private static async Task<List<AuthorDuplicatePair>> DetectAuthorsAsync(
        BookTrackerDbContext db,
        Dictionary<(int Lower, int Higher), DismissalInfo>? ignored,
        CancellationToken ct)
    {
        var authors = await db.Authors
            .AsNoTracking()
            .Select(a => new AuthorProjection(
                a.Id,
                a.Name,
                a.CanonicalAuthorId,
                a.CanonicalAuthor != null ? a.CanonicalAuthor.Name : null,
                a.Works.Count))
            .ToListAsync(ct);

        var pairs = new List<AuthorDuplicatePair>();
        var seen = new HashSet<(int, int)>();

        void Emit(AuthorProjection a, AuthorProjection b, string reason)
        {
            var key = a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);
            if (!seen.Add(key)) return;
            var lower = key.Item1 == a.Id ? a : b;
            var higher = key.Item1 == a.Id ? b : a;
            pairs.Add(new AuthorDuplicatePair(
                Lower: new DuplicateAuthorView(lower.Id, lower.Name, lower.WorkCount, lower.CanonicalAuthorId, lower.CanonicalName),
                Higher: new DuplicateAuthorView(higher.Id, higher.Name, higher.WorkCount, higher.CanonicalAuthorId, higher.CanonicalName),
                Dismissed: LookupDismissal(ignored, key.Item1, key.Item2),
                MatchReason: reason));
        }

        // Strategy 1 (tighter): full normalised name match. "J.R.R. Tolkien"
        // vs "JRR Tolkien" vs "J R R Tolkien" — all collapse to "jrr tolkien".
        foreach (var group in authors.GroupBy(a => DuplicateNormalization.Author(a.Name)))
        {
            if (string.IsNullOrEmpty(group.Key) || group.Count() < 2) continue;
            var members = group.OrderBy(a => a.Id).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    Emit(members[i], members[j], "Names normalise to the same value");
                }
            }
        }

        // Strategy 2 (looser): same normalised surname + same first-name
        // initial. Catches "Doug Preston" / "Douglas Preston" / "D Preston"
        // together. False positives (same surname, same initial, different
        // people) are handled by Dismiss.
        foreach (var group in authors.GroupBy(a => DuplicateNormalization.AuthorSurnameInitialKey(a.Name)))
        {
            if (string.IsNullOrEmpty(group.Key) || group.Count() < 2) continue;
            var members = group.OrderBy(a => a.Id).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    Emit(members[i], members[j], "Same surname and same first-name initial");
                }
            }
        }

        return pairs;
    }

    private sealed record AuthorProjection(
        int Id,
        string Name,
        int? CanonicalAuthorId,
        string? CanonicalName,
        int WorkCount);

    private static async Task<List<WorkDuplicatePair>> DetectWorksAsync(
        BookTrackerDbContext db,
        Dictionary<(int Lower, int Higher), DismissalInfo>? ignored,
        CancellationToken ct)
    {
        // Project an Author "fingerprint" — sorted, comma-joined Author IDs —
        // so works with the same author SET (regardless of order or co-author
        // count) bucket together for duplicate detection. Single-author works
        // get a single-id fingerprint; "Preston, Child" works share a
        // fingerprint distinct from a "Preston" solo work.
        //
        // AsSplitQuery: the projection traverses two collection navigations
        // (WorkAuthors twice, Books). Without splitting, EF emits a single
        // query with multi-LEFT-JOIN cartesian — slow plus the row count
        // multiplies materialisation memory.
        var works = (await db.Works
            .AsNoTracking()
            .AsSplitQuery()
            .Select(w => new
            {
                w.Id,
                w.Title,
                w.Subtitle,
                AuthorIdsJoined = string.Join(",", w.WorkAuthors.OrderBy(wa => wa.AuthorId).Select(wa => wa.AuthorId)),
                AuthorNames = string.Join(", ", w.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author.Name)),
                BookCount = w.Books.Count,
                FirstPublishedDate = w.FirstPublishedDate
            })
            .ToListAsync(ct))
            .Select(w => new
            {
                w.Id, w.Title, w.Subtitle, w.AuthorIdsJoined, w.AuthorNames, w.BookCount,
                FirstPublishedYear = w.FirstPublishedDate?.Year
            })
            .ToList();

        var pairs = new List<WorkDuplicatePair>();
        foreach (var group in works.GroupBy(w => (w.AuthorIdsJoined, DuplicateNormalization.Title(w.Title))))
        {
            if (string.IsNullOrEmpty(group.Key.Item2) || group.Count() < 2) continue;

            var members = group.OrderBy(w => w.Id).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var a = members[i];
                    var b = members[j];
                    var snapA = new DuplicateWorkView(a.Id, a.Title, a.Subtitle, a.AuthorNames, a.BookCount, a.FirstPublishedYear);
                    var snapB = new DuplicateWorkView(b.Id, b.Title, b.Subtitle, b.AuthorNames, b.BookCount, b.FirstPublishedYear);
                    pairs.Add(new WorkDuplicatePair(
                        Lower: snapA,
                        Higher: snapB,
                        Dismissed: LookupDismissal(ignored, a.Id, b.Id),
                        MatchReason: "Same author and normalised title"));
                }
            }
        }
        return pairs;
    }

    private static async Task<List<BookDuplicatePair>> DetectBooksAsync(
        BookTrackerDbContext db,
        Dictionary<(int Lower, int Higher), DismissalInfo>? ignored,
        CancellationToken ct)
    {
        // Project once; re-use for both detection strategies.
        //
        // AsSplitQuery: the projection touches Editions, Editions.Copies,
        // Works, and Works.WorkAuthors — four collection navigations. Without
        // splitting, EF SingleQuery cartesian-joins them and the row count
        // explodes (the warning logged in App Insights trace 3.a).
        var books = await db.Books
            .AsNoTracking()
            .InCollection()
            .AsSplitQuery()
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.DateAdded,
                EditionCount = b.Editions.Count,
                CopyCount = b.Editions.SelectMany(e => e.Copies).Count(),
                WorkIds = b.Works.Select(w => w.Id).ToList(),
                FirstAuthorId = b.Works
                    .SelectMany(w => w.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author).OrderBy(wa => wa.Order).Select(wa => (int?)wa.AuthorId))
                    .FirstOrDefault(),
                FirstAuthorName = b.Works
                    .SelectMany(w => w.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author).OrderBy(wa => wa.Order).Select(wa => wa.Author.Name))
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        // Dedupe key: each pair should appear only once across both strategies.
        var seen = new HashSet<(int, int)>();
        var pairs = new List<BookDuplicatePair>();

        void Emit(int lowerId, int higherId, string reason, Func<int, (int, string, string?, int, int, DateTime)> get)
        {
            if (!seen.Add((lowerId, higherId))) return;
            var l = get(lowerId);
            var h = get(higherId);
            var snapL = new DuplicateBookView(l.Item1, l.Item2, l.Item3, l.Item4, l.Item5, l.Item6);
            var snapH = new DuplicateBookView(h.Item1, h.Item2, h.Item3, h.Item4, h.Item5, h.Item6);
            pairs.Add(new BookDuplicatePair(snapL, snapH, LookupDismissal(ignored, lowerId, higherId), reason));
        }

        (int, string, string?, int, int, DateTime) Snapshot(int bookId)
        {
            var b = books.First(x => x.Id == bookId);
            return (b.Id, b.Title, b.FirstAuthorName, b.EditionCount, b.CopyCount, b.DateAdded);
        }

        // Strategy 1: same first-author + normalised title
        foreach (var group in books
            .Where(b => b.FirstAuthorId != null)
            .GroupBy(b => (b.FirstAuthorId!.Value, DuplicateNormalization.Title(b.Title))))
        {
            if (string.IsNullOrEmpty(group.Key.Item2) || group.Count() < 2) continue;
            var members = group.OrderBy(b => b.Id).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    Emit(members[i].Id, members[j].Id, "Same author and normalised title", Snapshot);
                }
            }
        }

        // Strategy 2: same first-author + same set of Work IDs (captured twice,
        // should've been one Book with multiple Editions).
        foreach (var group in books
            .Where(b => b.FirstAuthorId != null && b.WorkIds.Count > 0)
            .GroupBy(b => (b.FirstAuthorId!.Value, Key: string.Join(",", b.WorkIds.OrderBy(id => id)))))
        {
            if (group.Count() < 2) continue;
            var members = group.OrderBy(b => b.Id).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    Emit(members[i].Id, members[j].Id, "Same author and same set of Works", Snapshot);
                }
            }
        }

        return pairs;
    }

    private static async Task<List<EditionDuplicatePair>> DetectEditionsAsync(
        BookTrackerDbContext db,
        Dictionary<(int Lower, int Higher), DismissalInfo>? ignored,
        CancellationToken ct)
    {
        var inCollectionBooks = db.Books.InCollection();
        var editions = await db.Editions
            .AsNoTracking()
            .Where(e => inCollectionBooks.Any(b => b.Id == e.BookId))
            .Select(e => new EditionProjection(
                e.Id,
                e.Isbn,
                e.Format,
                e.PublisherId,
                e.Publisher != null ? e.Publisher.Name : null,
                e.DatePrinted,
                e.Copies.Count,
                e.BookId,
                e.Book.Title))
            .ToListAsync(ct);

        var pairs = new List<EditionDuplicatePair>();

        foreach (var bookGroup in editions.GroupBy(e => e.BookId))
        {
            foreach (var isbnGroup in bookGroup.Where(e => !string.IsNullOrWhiteSpace(e.Isbn)).GroupBy(e => e.Isbn!))
            {
                if (isbnGroup.Count() >= 2)
                {
                    EmitEditionPairs(isbnGroup.ToList(), "Same Book and same ISBN", ignored, pairs);
                }
            }

            foreach (var metaGroup in bookGroup
                .Where(e => string.IsNullOrWhiteSpace(e.Isbn))
                .GroupBy(e => (e.Format, e.PublisherId, e.DatePrinted)))
            {
                if (metaGroup.Count() >= 2)
                {
                    EmitEditionPairs(metaGroup.ToList(), "Same Book, no ISBN, matching format + publisher + date", ignored, pairs);
                }
            }
        }

        return pairs;
    }

    private static void EmitEditionPairs(
        IReadOnlyList<EditionProjection> members,
        string reason,
        Dictionary<(int Lower, int Higher), DismissalInfo>? ignored,
        List<EditionDuplicatePair> output)
    {
        var ordered = members.OrderBy(m => m.Id).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var a = ordered[i];
                var b = ordered[j];
                output.Add(new EditionDuplicatePair(
                    Lower: ToSnapshot(a),
                    Higher: ToSnapshot(b),
                    Dismissed: LookupDismissal(ignored, a.Id, b.Id),
                    MatchReason: reason));
            }
        }

        static DuplicateEditionView ToSnapshot(EditionProjection e) => new(
            e.Id, e.Isbn, e.Format, e.PublisherName, e.DatePrinted, e.CopyCount, e.BookId, e.BookTitle);
    }

    private sealed record EditionProjection(
        int Id,
        string? Isbn,
        BookFormat Format,
        int? PublisherId,
        string? PublisherName,
        DateOnly? DatePrinted,
        int CopyCount,
        int BookId,
        string BookTitle);
}
