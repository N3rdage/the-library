using BookTracker.Application.Formatting;
using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

// Read-model for the Work-merge preview page (/duplicates/merge/work/{a}/{b}).
// The merge write itself is the MergeWorks command. Relocated from the Web
// WorkMergeService loader in PR6.
public sealed record GetWorkMergePreview(int IdA, int IdB) : IQuery<WorkMergeLoadResult>;

public record WorkMergeLoadResult(
    WorkMergeDetail? Lower,
    WorkMergeDetail? Higher,
    string? IncompatibilityReason,
    // How many Books currently contain BOTH works. Post-merge those Books
    // will just contain the winner — the loser-side BookWork row gets
    // dropped. Surfaced on the preview so the user isn't surprised by a
    // Works-count drop on any compendium that had both.
    int SharedBookCount);

public record WorkMergeDetail(
    int Id,
    string Title,
    string? Subtitle,
    string AuthorName,
    int? FirstPublishedYear,
    IReadOnlyList<string> GenreNames,
    int BookCount,
    IReadOnlyList<string> SampleBookTitles,
    // Best-effort cover URL — picks the DefaultCoverArtUrl of a Book that
    // contains ONLY this Work (a standalone edition whose cover faithfully
    // represents the Work), falling back to any Book containing it.
    string? CoverArtUrl);

public sealed class GetWorkMergePreviewHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetWorkMergePreview, WorkMergeLoadResult>
{
    private const int SampleBookLimit = 5;

    public async Task<WorkMergeLoadResult> HandleAsync(GetWorkMergePreview query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var (lowerId, higherId) = query.IdA < query.IdB ? (query.IdA, query.IdB) : (query.IdB, query.IdA);

        var lower = await LoadDetailAsync(db, lowerId, ct);
        var higher = await LoadDetailAsync(db, higherId, ct);

        string? incompatibility = null;
        var sharedBookCount = 0;

        if (lower is not null && higher is not null)
        {
            // Detection already blocks by author set, so this is a defensive
            // check for direct URL hits. Both Works must credit the same set
            // of authors (post-PR2 multi-author cutover); a single mismatch
            // anywhere blocks the merge with the same message as before.
            // Compare the SET of distinct AuthorIds, not the full row list —
            // post-Phase-A a Work can credit the same Author in multiple roles
            // (Tolkien as Author + Illustrator on *The Hobbit*), and that
            // shouldn't make the merge incompatible with a Work that credits
            // Tolkien just as Author.
            var lowerAuthorIds = (await db.WorkAuthors
                .AsNoTracking()
                .Where(wa => wa.WorkId == lowerId)
                .Select(wa => wa.AuthorId)
                .Distinct()
                .ToListAsync(ct))
                .OrderBy(id => id)
                .ToList();
            var higherAuthorIds = (await db.WorkAuthors
                .AsNoTracking()
                .Where(wa => wa.WorkId == higherId)
                .Select(wa => wa.AuthorId)
                .Distinct()
                .ToListAsync(ct))
                .OrderBy(id => id)
                .ToList();
            if (!lowerAuthorIds.SequenceEqual(higherAuthorIds))
            {
                incompatibility = "Works belong to different authors. Merge the authors on /duplicates first, then come back.";
            }
            else
            {
                sharedBookCount = await CountSharedBooksAsync(db, lowerId, higherId, ct);
            }
        }

        return new WorkMergeLoadResult(lower, higher, incompatibility, sharedBookCount);
    }

    private static async Task<WorkMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var work = await db.Works
            .AsNoTracking()
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Genres)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (work is null) return null;

        var bookCount = await db.Books.CountAsync(b => b.Works.Any(w => w.Id == id), ct);
        var sampleBookTitles = await db.Books
            .Where(b => b.Works.Any(w => w.Id == id))
            .OrderBy(b => b.Title)
            .Take(SampleBookLimit)
            .Select(b => b.Title)
            .ToListAsync(ct);

        // Cover pick: prefer a single-Work Book, fall back to any Book
        // containing this Work (shared rule — BookCovers).
        var fallbackCover = await Books.BookCovers.PickAsync(
            db.Books.Where(b => b.Works.Any(w => w.Id == id)), ct);

        return new WorkMergeDetail(
            work.Id, work.Title, work.Subtitle,
            WorkAuthorshipFormatter.Display(work),
            work.FirstPublishedDate?.Year,
            work.Genres.Select(g => g.Name).OrderBy(n => n).ToList(),
            bookCount, sampleBookTitles,
            fallbackCover);
    }

    private static async Task<int> CountSharedBooksAsync(BookTrackerDbContext db, int lowerId, int higherId, CancellationToken ct)
    {
        return await db.Books
            .CountAsync(b => b.Works.Any(w => w.Id == lowerId)
                          && b.Works.Any(w => w.Id == higherId), ct);
    }
}
