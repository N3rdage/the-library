using System.Text;
using System.Text.RegularExpressions;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IDuplicateDetectionService
{
    Task<DuplicateReport> DetectAllAsync(CancellationToken ct = default);
    Task DismissAsync(DuplicateEntityType type, int idA, int idB, string? note, CancellationToken ct = default);
    Task UnignoreAsync(int ignoredDuplicateId, CancellationToken ct = default);
}

public record DuplicateReport(
    IReadOnlyList<AuthorDuplicatePair> Authors,
    IReadOnlyList<WorkDuplicatePair> Works,
    IReadOnlyList<BookDuplicatePair> Books,
    IReadOnlyList<EditionDuplicatePair> Editions);

public record DismissalInfo(int IgnoredDuplicateId, DateTime DismissedAt, string? Note);

public record AuthorSnapshot(int Id, string Name, int WorkCount, int? CanonicalAuthorId, string? CanonicalName);
public record AuthorDuplicatePair(AuthorSnapshot Lower, AuthorSnapshot Higher, DismissalInfo? Dismissed, string MatchReason);

public record WorkSnapshot(int Id, string Title, string? Subtitle, string AuthorName, int BookCount, int? FirstPublishedYear);
public record WorkDuplicatePair(WorkSnapshot Lower, WorkSnapshot Higher, DismissalInfo? Dismissed, string MatchReason);

public record BookSnapshot(int Id, string Title, string? AuthorName, int EditionCount, int CopyCount, DateTime DateAdded);
public record BookDuplicatePair(BookSnapshot Lower, BookSnapshot Higher, DismissalInfo? Dismissed, string MatchReason);

public record EditionSnapshot(int Id, string? Isbn, BookFormat Format, string? PublisherName, DateOnly? DatePrinted, int CopyCount, int BookId, string BookTitle);
public record EditionDuplicatePair(EditionSnapshot Lower, EditionSnapshot Higher, DismissalInfo? Dismissed, string MatchReason);

public class DuplicateDetectionService(IDbContextFactory<BookTrackerDbContext> dbFactory) : IDuplicateDetectionService
{
    public async Task<DuplicateReport> DetectAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        await CleanupOrphansAsync(db, ct);

        var ignoredLookup = await BuildIgnoredLookupAsync(db, ct);

        return new DuplicateReport(
            Authors: await DetectAuthorsAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Author), ct),
            Works: await DetectWorksAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Work), ct),
            Books: await DetectBooksAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Book), ct),
            Editions: await DetectEditionsAsync(db, ignoredLookup.GetValueOrDefault(DuplicateEntityType.Edition), ct));
    }

    public async Task DismissAsync(DuplicateEntityType type, int idA, int idB, string? note, CancellationToken ct = default)
    {
        if (idA == idB) return;
        var (lower, higher) = idA < idB ? (idA, idB) : (idB, idA);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.IgnoredDuplicates
            .FirstOrDefaultAsync(d => d.EntityType == type && d.LowerId == lower && d.HigherId == higher, ct);
        if (existing is not null) return;

        db.IgnoredDuplicates.Add(new IgnoredDuplicate
        {
            EntityType = type,
            LowerId = lower,
            HigherId = higher,
            DismissedAt = DateTime.UtcNow,
            Note = note
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task UnignoreAsync(int ignoredDuplicateId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.IgnoredDuplicates.FirstOrDefaultAsync(d => d.Id == ignoredDuplicateId, ct);
        if (row is null) return;
        db.IgnoredDuplicates.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private static async Task CleanupOrphansAsync(BookTrackerDbContext db, CancellationToken ct)
    {
        var rows = await db.IgnoredDuplicates.ToListAsync(ct);
        if (rows.Count == 0) return;

        var authorIds = await db.Authors.Select(a => a.Id).ToHashSetAsync(ct);
        var workIds = await db.Works.Select(w => w.Id).ToHashSetAsync(ct);
        var bookIds = await db.Books.Select(b => b.Id).ToHashSetAsync(ct);
        var editionIds = await db.Editions.Select(e => e.Id).ToHashSetAsync(ct);

        var orphans = rows.Where(r =>
        {
            var ids = r.EntityType switch
            {
                DuplicateEntityType.Author => authorIds,
                DuplicateEntityType.Work => workIds,
                DuplicateEntityType.Book => bookIds,
                DuplicateEntityType.Edition => editionIds,
                _ => null
            };
            return ids is null || !ids.Contains(r.LowerId) || !ids.Contains(r.HigherId);
        }).ToList();

        if (orphans.Count > 0)
        {
            db.IgnoredDuplicates.RemoveRange(orphans);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<Dictionary<DuplicateEntityType, Dictionary<(int Lower, int Higher), IgnoredDuplicate>>> BuildIgnoredLookupAsync(
        BookTrackerDbContext db, CancellationToken ct)
    {
        var all = await db.IgnoredDuplicates.ToListAsync(ct);
        return all
            .GroupBy(i => i.EntityType)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(i => (i.LowerId, i.HigherId), i => i));
    }

    private static DismissalInfo? LookupDismissal(
        Dictionary<(int Lower, int Higher), IgnoredDuplicate>? ignored,
        int lower, int higher)
    {
        if (ignored is null) return null;
        return ignored.TryGetValue((lower, higher), out var row)
            ? new DismissalInfo(row.Id, row.DismissedAt, row.Note)
            : null;
    }

    private static async Task<List<AuthorDuplicatePair>> DetectAuthorsAsync(
        BookTrackerDbContext db,
        Dictionary<(int Lower, int Higher), IgnoredDuplicate>? ignored,
        CancellationToken ct)
    {
        var authors = await db.Authors
            .Include(a => a.CanonicalAuthor)
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
                Lower: new AuthorSnapshot(lower.Id, lower.Name, lower.WorkCount, lower.CanonicalAuthorId, lower.CanonicalName),
                Higher: new AuthorSnapshot(higher.Id, higher.Name, higher.WorkCount, higher.CanonicalAuthorId, higher.CanonicalName),
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
        Dictionary<(int Lower, int Higher), IgnoredDuplicate>? ignored,
        CancellationToken ct)
    {
        var works = (await db.Works
            .Include(w => w.Author)
            .Select(w => new
            {
                w.Id,
                w.Title,
                w.Subtitle,
                w.AuthorId,
                AuthorName = w.Author.Name,
                BookCount = w.Books.Count,
                FirstPublishedDate = w.FirstPublishedDate
            })
            .ToListAsync(ct))
            .Select(w => new
            {
                w.Id, w.Title, w.Subtitle, w.AuthorId, w.AuthorName, w.BookCount,
                FirstPublishedYear = w.FirstPublishedDate?.Year
            })
            .ToList();

        var pairs = new List<WorkDuplicatePair>();
        foreach (var group in works.GroupBy(w => (w.AuthorId, DuplicateNormalization.Title(w.Title))))
        {
            if (string.IsNullOrEmpty(group.Key.Item2) || group.Count() < 2) continue;

            var members = group.OrderBy(w => w.Id).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var a = members[i];
                    var b = members[j];
                    var snapA = new WorkSnapshot(a.Id, a.Title, a.Subtitle, a.AuthorName, a.BookCount, a.FirstPublishedYear);
                    var snapB = new WorkSnapshot(b.Id, b.Title, b.Subtitle, b.AuthorName, b.BookCount, b.FirstPublishedYear);
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
        Dictionary<(int Lower, int Higher), IgnoredDuplicate>? ignored,
        CancellationToken ct)
    {
        // Project once; re-use for both detection strategies.
        var books = await db.Books
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.DateAdded,
                EditionCount = b.Editions.Count,
                CopyCount = b.Editions.SelectMany(e => e.Copies).Count(),
                WorkIds = b.Works.Select(w => w.Id).ToList(),
                FirstAuthorId = b.Works.Select(w => (int?)w.AuthorId).FirstOrDefault(),
                FirstAuthorName = b.Works.Select(w => w.Author.Name).FirstOrDefault()
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
            var snapL = new BookSnapshot(l.Item1, l.Item2, l.Item3, l.Item4, l.Item5, l.Item6);
            var snapH = new BookSnapshot(h.Item1, h.Item2, h.Item3, h.Item4, h.Item5, h.Item6);
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
        Dictionary<(int Lower, int Higher), IgnoredDuplicate>? ignored,
        CancellationToken ct)
    {
        var editions = await db.Editions
            .Include(e => e.Publisher)
            .Include(e => e.Book)
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
        Dictionary<(int Lower, int Higher), IgnoredDuplicate>? ignored,
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

        static EditionSnapshot ToSnapshot(EditionProjection e) => new(
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

internal static class DuplicateNormalization
{
    private static readonly Regex PunctuationAndSymbols = new(@"[\p{P}\p{S}]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex LeadingArticle = new(@"^(the|a|an)\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Normalise an author name so "J.R.R. Tolkien", "JRR Tolkien", and
    // "J R R Tolkien" all collapse to "jrr tolkien". Single-letter runs
    // (typically initials) are joined into one token.
    public static string Author(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var lower = name.ToLowerInvariant();
        var depunct = PunctuationAndSymbols.Replace(lower, " ");
        var tokens = depunct.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var output = new List<string>();
        var run = new StringBuilder();
        foreach (var tok in tokens)
        {
            if (tok.Length == 1)
            {
                run.Append(tok);
            }
            else
            {
                if (run.Length > 0)
                {
                    output.Add(run.ToString());
                    run.Clear();
                }
                output.Add(tok);
            }
        }
        if (run.Length > 0) output.Add(run.ToString());

        return string.Join(' ', output);
    }

    // Secondary author key: "<surname>|<first-name-initial>". Returns empty
    // if the normalised name has fewer than two tokens (can't identify a
    // first-name initial). Used by the looser author-dup detection pass so
    // "Doug Preston" / "Douglas Preston" / "D Preston" surface as candidates.
    public static string AuthorSurnameInitialKey(string? name)
    {
        var normalized = Author(name);
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return "";
        var surname = tokens[^1];
        var firstInitial = tokens[0][0];
        return $"{surname}|{firstInitial}";
    }

    // Normalise a title for comparison. Lowercase, strip punctuation, collapse
    // whitespace, drop a leading article ("The ", "A ", "An ").
    public static string Title(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var lower = title.ToLowerInvariant();
        var depunct = PunctuationAndSymbols.Replace(lower, " ");
        var collapsed = Whitespace.Replace(depunct, " ").Trim();
        return LeadingArticle.Replace(collapsed, "");
    }
}
