using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IBookMergeService
{
    Task<BookMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default);
    Task<BookMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default);
}

public record BookMergeLoadResult(
    BookMergeDetail? Lower,
    BookMergeDetail? Higher);

public record BookMergeDetail(
    int Id,
    string Title,
    string? AuthorName,
    BookCategory Category,
    BookStatus Status,
    int Rating,
    string? Notes,
    DateTime DateAdded,
    int EditionCount,
    int CopyCount,
    IReadOnlyList<string> WorkTitles,
    IReadOnlyList<string> TagNames,
    string? CoverArtUrl);

public record BookMergeResult(
    bool Success,
    string? ErrorMessage,
    int EditionsReassigned,
    int WorksUnioned,
    int TagsUnioned,
    int FieldsAutoFilled,
    string? WinnerTitle,
    string? LoserTitle);

// Book merge has no structural incompatibility path. The Edition unique-ISBN
// index is global (filtered WHERE Isbn IS NOT NULL), so two Books can never
// hold editions with overlapping non-null ISBNs. Works + Tags just union.
// Any resulting Edition-level duplicates (two no-ISBN editions with matching
// format/publisher/date after the merge) surface on /duplicates for the user
// to clean up via Edition merge.
public class BookMergeService(IDbContextFactory<BookTrackerDbContext> dbFactory) : IBookMergeService
{
    public async Task<BookMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (lowerId, higherId) = idA < idB ? (idA, idB) : (idB, idA);
        return new BookMergeLoadResult(
            await LoadDetailAsync(db, lowerId, ct),
            await LoadDetailAsync(db, higherId, ct));
    }

    public async Task<BookMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default)
    {
        if (winnerId == loserId)
        {
            return Failure("Winner and loser cannot be the same Book.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Works)
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == winnerId, ct);
        var loser = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Works)
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == loserId, ct);
        if (winner is null || loser is null)
        {
            return Failure("One or both Books could not be found — they may already have been merged or deleted.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ─── Auto-fill empty winner fields from loser ──────────────────
        // Never overwrites a value winner already has. Rating 0 is treated
        // as "unrated" (the stars-1-to-5 UI can't produce an active 0) so
        // it behaves like an empty field for enrichment purposes.
        var fieldsAutoFilled = 0;

        if (string.IsNullOrWhiteSpace(winner.Notes) && !string.IsNullOrWhiteSpace(loser.Notes))
        {
            winner.Notes = loser.Notes;
            fieldsAutoFilled++;
        }

        if (string.IsNullOrWhiteSpace(winner.DefaultCoverArtUrl) && !string.IsNullOrWhiteSpace(loser.DefaultCoverArtUrl))
        {
            winner.DefaultCoverArtUrl = loser.DefaultCoverArtUrl;
            fieldsAutoFilled++;
        }

        if (winner.Rating == 0 && loser.Rating > 0)
        {
            winner.Rating = loser.Rating;
            fieldsAutoFilled++;
        }

        // ─── Union Works ───────────────────────────────────────────────
        var winnerWorkIds = winner.Works.Select(w => w.Id).ToHashSet();
        var worksUnioned = 0;
        foreach (var work in loser.Works.Where(w => !winnerWorkIds.Contains(w.Id)).ToList())
        {
            winner.Works.Add(work);
            winnerWorkIds.Add(work.Id);
            worksUnioned++;
        }
        loser.Works.Clear();

        // ─── Union Tags ────────────────────────────────────────────────
        var winnerTagIds = winner.Tags.Select(t => t.Id).ToHashSet();
        var tagsUnioned = 0;
        foreach (var tag in loser.Tags.Where(t => !winnerTagIds.Contains(t.Id)).ToList())
        {
            winner.Tags.Add(tag);
            winnerTagIds.Add(tag.Id);
            tagsUnioned++;
        }
        loser.Tags.Clear();

        // ─── Reassign Editions ─────────────────────────────────────────
        // Editions (and their Copies, which belong to Edition not Book)
        // move as a unit by flipping the BookId FK.
        var editionsReassigned = 0;
        foreach (var edition in loser.Editions.ToList())
        {
            edition.BookId = winner.Id;
            editionsReassigned++;
        }
        loser.Editions.Clear();

        var staleIgnores = await db.IgnoredDuplicates
            .Where(d => d.EntityType == DuplicateEntityType.Book
                    && (d.LowerId == loser.Id || d.HigherId == loser.Id))
            .ToListAsync(ct);
        db.IgnoredDuplicates.RemoveRange(staleIgnores);

        db.Books.Remove(loser);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new BookMergeResult(
            Success: true,
            ErrorMessage: null,
            EditionsReassigned: editionsReassigned,
            WorksUnioned: worksUnioned,
            TagsUnioned: tagsUnioned,
            FieldsAutoFilled: fieldsAutoFilled,
            WinnerTitle: winner.Title,
            LoserTitle: loser.Title);
    }

    private static async Task<BookMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var book = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Works).ThenInclude(w => w.Author)
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return null;

        var copyCount = await db.Copies.CountAsync(c => c.Edition.BookId == id, ct);

        // Cover pick: Book's own DefaultCoverArtUrl; fall back to the first
        // Edition's CoverUrl.
        var cover = !string.IsNullOrWhiteSpace(book.DefaultCoverArtUrl)
            ? book.DefaultCoverArtUrl
            : book.Editions.Select(e => e.CoverUrl).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        var firstAuthor = book.Works.FirstOrDefault()?.Author.Name;

        return new BookMergeDetail(
            book.Id, book.Title, firstAuthor,
            book.Category, book.Status, book.Rating, book.Notes, book.DateAdded,
            book.Editions.Count, copyCount,
            book.Works.Select(w => w.Title).OrderBy(t => t).ToList(),
            book.Tags.Select(t => t.Name).OrderBy(t => t).ToList(),
            cover);
    }

    private static BookMergeResult Failure(string message) =>
        new(false, message, 0, 0, 0, 0, null, null);
}
