using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IWorkMergeService
{
    Task<WorkMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default);
}

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
    string? SeriesName,
    string? SeriesOrderLabel,
    IReadOnlyList<string> GenreNames,
    int BookCount,
    IReadOnlyList<string> SampleBookTitles,
    // Best-effort cover URL — picks the DefaultCoverArtUrl of a Book that
    // contains ONLY this Work (a standalone edition whose cover faithfully
    // represents the Work), falling back to any Book containing it.
    string? CoverArtUrl);

// Read-only loader for the Work-merge preview page. The merge write itself is
// the MergeWorks command in BookTracker.Application.Works (PR5). These reads
// stay here until the read-model relocation (PR6).
public class WorkMergeService(IDbContextFactory<BookTrackerDbContext> dbFactory) : IWorkMergeService
{
    private const int SampleBookLimit = 5;

    public async Task<WorkMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var (lowerId, higherId) = idA < idB ? (idA, idB) : (idB, idA);

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
                .Where(wa => wa.WorkId == lowerId)
                .Select(wa => wa.AuthorId)
                .Distinct()
                .ToListAsync(ct))
                .OrderBy(id => id)
                .ToList();
            var higherAuthorIds = (await db.WorkAuthors
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
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Series)
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

        // Cover pick: prefer a single-Work Book (its cover represents this
        // Work faithfully); fall back to any Book.
        var singleWorkBookCover = await db.Books
            .Where(b => b.Works.Any(w => w.Id == id) && b.Works.Count == 1)
            .Select(b => b.DefaultCoverArtUrl)
            .FirstOrDefaultAsync(ct);
        var fallbackCover = singleWorkBookCover is null
            ? await db.Books
                .Where(b => b.Works.Any(w => w.Id == id))
                .Select(b => b.DefaultCoverArtUrl)
                .FirstOrDefaultAsync(ct)
            : singleWorkBookCover;

        return new WorkMergeDetail(
            work.Id, work.Title, work.Subtitle,
            WorkAuthorshipFormatter.Display(work),
            work.FirstPublishedDate?.Year,
            work.Series?.Name,
            SeriesOrderParser.Format(work.SeriesOrder, work.SeriesOrderDisplay),
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
