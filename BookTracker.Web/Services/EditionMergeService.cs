using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IEditionMergeService
{
    Task<EditionMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default);
    Task<EditionMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default);
}

public record EditionMergeLoadResult(
    EditionMergeDetail? Lower,
    EditionMergeDetail? Higher,
    string? IncompatibilityReason);

public record EditionMergeDetail(
    int Id,
    string? Isbn,
    BookFormat Format,
    string? PublisherName,
    DateOnly? DatePrinted,
    DatePrecision DatePrintedPrecision,
    int CopyCount,
    int BookId,
    string BookTitle,
    string? CoverArtUrl);

public record EditionMergeResult(
    bool Success,
    string? ErrorMessage,
    int CopiesReassigned,
    int FieldsAutoFilled,
    string? WinnerLabel,
    string? LoserLabel);

// Same shape as WorkMergeService: transactional, reassigns Copy.EditionId
// from loser to winner, auto-fills empty winner fields from loser
// (DatePrinted+Precision pair, CoverUrl, PublisherId), clears stale
// IgnoredDuplicate rows mentioning the loser, deletes the loser Edition.
//
// Compatibility: both Editions must belong to the same Book. Detection
// already blocks cross-Book pairs, but the service guards direct-URL hits
// — a cross-Book edition merge would be a category error (if they're the
// same Edition, the Books must be dupes, and the Book-level merge should
// happen first).
public class EditionMergeService(IDbContextFactory<BookTrackerDbContext> dbFactory) : IEditionMergeService
{
    public async Task<EditionMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var (lowerId, higherId) = idA < idB ? (idA, idB) : (idB, idA);

        var lower = await LoadDetailAsync(db, lowerId, ct);
        var higher = await LoadDetailAsync(db, higherId, ct);

        string? incompatibility = null;
        if (lower is not null && higher is not null && lower.BookId != higher.BookId)
        {
            incompatibility = $"These Editions belong to different Books (\"{lower.BookTitle}\" vs \"{higher.BookTitle}\"). If the Books themselves are duplicates, merge them first on /duplicates.";
        }

        return new EditionMergeLoadResult(lower, higher, incompatibility);
    }

    public async Task<EditionMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default)
    {
        if (winnerId == loserId)
        {
            return Failure("Winner and loser cannot be the same Edition.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Editions
            .Include(e => e.Copies)
            .FirstOrDefaultAsync(e => e.Id == winnerId, ct);
        var loser = await db.Editions
            .Include(e => e.Copies)
            .FirstOrDefaultAsync(e => e.Id == loserId, ct);
        if (winner is null || loser is null)
        {
            return Failure("One or both Editions could not be found — they may already have been merged or deleted.");
        }

        if (winner.BookId != loser.BookId)
        {
            return Failure("These Editions belong to different Books. Merge the Books first on /duplicates.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Auto-fill empty winner fields from loser. Never overwrites a value
        // winner already has. Date+Precision move as a pair.
        var fieldsAutoFilled = 0;

        if (winner.DatePrinted is null && loser.DatePrinted is not null)
        {
            winner.DatePrinted = loser.DatePrinted;
            winner.DatePrintedPrecision = loser.DatePrintedPrecision;
            fieldsAutoFilled++;
        }

        if (string.IsNullOrWhiteSpace(winner.CoverUrl) && !string.IsNullOrWhiteSpace(loser.CoverUrl))
        {
            winner.CoverUrl = loser.CoverUrl;
            fieldsAutoFilled++;
        }

        if (winner.PublisherId is null && loser.PublisherId is not null)
        {
            winner.PublisherId = loser.PublisherId;
            fieldsAutoFilled++;
        }

        if (string.IsNullOrWhiteSpace(winner.Isbn) && !string.IsNullOrWhiteSpace(loser.Isbn))
        {
            winner.Isbn = loser.Isbn;
            fieldsAutoFilled++;
        }

        // Reassign copies from loser to winner.
        var copiesReassigned = 0;
        foreach (var copy in loser.Copies.ToList())
        {
            copy.EditionId = winner.Id;
            copiesReassigned++;
        }

        // Reload nav collection so loser.Copies doesn't hold references EF
        // can't resolve at delete time.
        loser.Copies.Clear();

        var staleIgnores = await db.IgnoredDuplicates
            .Where(d => d.EntityType == DuplicateEntityType.Edition
                    && (d.LowerId == loser.Id || d.HigherId == loser.Id))
            .ToListAsync(ct);
        db.IgnoredDuplicates.RemoveRange(staleIgnores);

        db.Editions.Remove(loser);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new EditionMergeResult(
            Success: true,
            ErrorMessage: null,
            CopiesReassigned: copiesReassigned,
            FieldsAutoFilled: fieldsAutoFilled,
            WinnerLabel: FormatLabel(winner),
            LoserLabel: FormatLabel(loser));
    }

    private static async Task<EditionMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var edition = await db.Editions
            .Include(e => e.Publisher)
            .Include(e => e.Book)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (edition is null) return null;

        var copyCount = await db.Copies.CountAsync(c => c.EditionId == id, ct);

        // Cover pick: Edition's own CoverUrl; fall back to Book's default.
        var cover = !string.IsNullOrWhiteSpace(edition.CoverUrl)
            ? edition.CoverUrl
            : edition.Book.DefaultCoverArtUrl;

        return new EditionMergeDetail(
            edition.Id,
            edition.Isbn,
            edition.Format,
            edition.Publisher?.Name,
            edition.DatePrinted,
            edition.DatePrintedPrecision,
            copyCount,
            edition.BookId,
            edition.Book.Title,
            cover);
    }

    private static string FormatLabel(Edition e)
    {
        // Friendly label for banner: ISBN if present, else format + publisher.
        if (!string.IsNullOrWhiteSpace(e.Isbn)) return $"{e.Format} · ISBN {e.Isbn}";
        return $"{e.Format}" + (e.PublisherId.HasValue ? " · (publisher known)" : "");
    }

    private static EditionMergeResult Failure(string message) =>
        new(false, message, 0, 0, null, null);
}
