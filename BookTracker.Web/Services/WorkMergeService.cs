using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IWorkMergeService
{
    Task<WorkMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default);
    Task<WorkMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default);
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
    int? SeriesOrder,
    IReadOnlyList<string> GenreNames,
    int BookCount,
    IReadOnlyList<string> SampleBookTitles,
    // Best-effort cover URL — picks the DefaultCoverArtUrl of a Book that
    // contains ONLY this Work (a standalone edition whose cover faithfully
    // represents the Work), falling back to any Book containing it.
    string? CoverArtUrl);

public record WorkMergeResult(
    bool Success,
    string? ErrorMessage,
    int BooksReassigned,
    int BooksAlreadyShared,
    // Count of winner fields that were auto-filled from the loser because
    // the winner had the field empty/null. Includes genre-union as one
    // "field" contribution if any genres were added.
    int FieldsAutoFilled,
    string? WinnerTitle,
    string? LoserTitle);

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
            // Detection already blocks by AuthorId so this is mostly a
            // defensive check for direct URL hits.
            var lowerRaw = await db.Works.AsNoTracking().FirstAsync(w => w.Id == lowerId, ct);
            var higherRaw = await db.Works.AsNoTracking().FirstAsync(w => w.Id == higherId, ct);
            if (lowerRaw.AuthorId != higherRaw.AuthorId)
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

    public async Task<WorkMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default)
    {
        if (winnerId == loserId)
        {
            return Failure("Winner and loser cannot be the same Work.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Works
            .Include(w => w.Books)
            .Include(w => w.Genres)
            .FirstOrDefaultAsync(w => w.Id == winnerId, ct);
        var loser = await db.Works
            .Include(w => w.Books)
            .Include(w => w.Genres)
            .FirstOrDefaultAsync(w => w.Id == loserId, ct);
        if (winner is null || loser is null)
        {
            return Failure("One or both Works could not be found — they may already have been merged or deleted.");
        }

        if (winner.AuthorId != loser.AuthorId)
        {
            return Failure("Works belong to different authors. Merge the authors first on /duplicates.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ─── Auto-fill empty winner fields from loser ──────────────────
        // Only fills gaps — never overwrites a value winner already has.
        // Paired fields move together (date+precision, series+order) so the
        // two halves stay consistent.
        var fieldsAutoFilled = 0;

        if (string.IsNullOrWhiteSpace(winner.Subtitle) && !string.IsNullOrWhiteSpace(loser.Subtitle))
        {
            winner.Subtitle = loser.Subtitle;
            fieldsAutoFilled++;
        }

        if (winner.FirstPublishedDate is null && loser.FirstPublishedDate is not null)
        {
            winner.FirstPublishedDate = loser.FirstPublishedDate;
            winner.FirstPublishedDatePrecision = loser.FirstPublishedDatePrecision;
            fieldsAutoFilled++;
        }

        if (winner.SeriesId is null && loser.SeriesId is not null)
        {
            winner.SeriesId = loser.SeriesId;
            winner.SeriesOrder = loser.SeriesOrder;
            fieldsAutoFilled++;
        }

        var winnerGenreIds = winner.Genres.Select(g => g.Id).ToHashSet();
        var genresAdded = 0;
        foreach (var g in loser.Genres.Where(g => !winnerGenreIds.Contains(g.Id)).ToList())
        {
            winner.Genres.Add(g);
            genresAdded++;
        }
        if (genresAdded > 0) fieldsAutoFilled++;

        // ─── Reassign BookWork rows ────────────────────────────────────
        // Book-contains-both case: for each Book in loser.Books where winner
        // is NOT already attached, add winner; otherwise skip (winner stays,
        // loser just gets removed when we clear loser.Books).
        var winnerBookIds = winner.Books.Select(b => b.Id).ToHashSet();

        var booksReassigned = 0;
        var booksAlreadyShared = 0;
        foreach (var book in loser.Books.ToList())
        {
            if (winnerBookIds.Contains(book.Id))
            {
                booksAlreadyShared++;
            }
            else
            {
                book.Works.Add(winner);
                winnerBookIds.Add(book.Id);
                booksReassigned++;
            }
        }

        loser.Books.Clear();

        var staleIgnores = await db.IgnoredDuplicates
            .Where(d => d.EntityType == DuplicateEntityType.Work
                    && (d.LowerId == loser.Id || d.HigherId == loser.Id))
            .ToListAsync(ct);
        db.IgnoredDuplicates.RemoveRange(staleIgnores);

        db.Works.Remove(loser);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new WorkMergeResult(
            Success: true,
            ErrorMessage: null,
            BooksReassigned: booksReassigned,
            BooksAlreadyShared: booksAlreadyShared,
            FieldsAutoFilled: fieldsAutoFilled,
            WinnerTitle: winner.Title,
            LoserTitle: loser.Title);
    }

    private static async Task<WorkMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var work = await db.Works
            .Include(w => w.Author)
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
            work.Author.Name,
            work.FirstPublishedDate?.Year,
            work.Series?.Name,
            work.SeriesOrder,
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

    private static WorkMergeResult Failure(string message) =>
        new(false, message, 0, 0, 0, null, null);
}
