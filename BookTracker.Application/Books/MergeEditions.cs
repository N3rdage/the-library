using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Merges the loser Edition into the winner (both must belong to the
/// same Book): reassigns the loser's Copies, auto-fills empty winner fields
/// (DatePrinted+precision, CoverUrl, PublisherId, Isbn) from the loser, clears
/// stale dismissed-dup rows, then deletes the loser Edition. Edition ops live
/// under the Book aggregate. Lifted verbatim from the old EditionMergeService
/// write path (PR5, back-end refactor).</summary>
public sealed record MergeEditions(int WinnerId, int LoserId) : ICommand<EditionMergeResult>;

public record EditionMergeResult(
    bool Success,
    string? ErrorMessage,
    int CopiesReassigned,
    int FieldsAutoFilled,
    string? WinnerLabel,
    string? LoserLabel);

// Compatibility: both Editions must belong to the same Book. Detection already
// blocks cross-Book pairs, but the handler guards direct-URL hits — a cross-Book
// edition merge would be a category error (if they're the same Edition, the
// Books must be dupes, and the Book-level merge should happen first).
public sealed class MergeEditionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<MergeEditions, EditionMergeResult>
{
    public async Task<EditionMergeResult> HandleAsync(MergeEditions command, CancellationToken ct = default)
    {
        if (command.WinnerId == command.LoserId)
        {
            return Failure("Winner and loser cannot be the same Edition.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Editions
            .Include(e => e.Copies)
            .FirstOrDefaultAsync(e => e.Id == command.WinnerId, ct);
        var loser = await db.Editions
            .Include(e => e.Copies)
            .FirstOrDefaultAsync(e => e.Id == command.LoserId, ct);
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

    private static string FormatLabel(Edition e)
    {
        // Friendly label for banner: ISBN if present, else format + publisher.
        if (!string.IsNullOrWhiteSpace(e.Isbn)) return $"{e.Format} · ISBN {e.Isbn}";
        return $"{e.Format}" + (e.PublisherId.HasValue ? " · (publisher known)" : "");
    }

    private static EditionMergeResult Failure(string message) =>
        new(false, message, 0, 0, null, null);
}
