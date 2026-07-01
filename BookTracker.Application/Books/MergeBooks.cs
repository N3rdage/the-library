using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Merges the loser Book into the winner: reassigns the loser's
/// Editions (with their Copies), unions its Works and Tags, auto-fills empty
/// winner fields (notes, cover, rating-if-unrated, series membership) from the
/// loser, then soft-deletes the loser as a tombstone for the catalog snapshot's
/// deletedIds[]. Already transactional + aggregate-shaped — lifted from the old
/// BookMergeService write path (PR5), with series-carry added when series moved
/// Work→Book (TODO #56).</summary>
public sealed record MergeBooks(int WinnerId, int LoserId) : ICommand<BookMergeResult>;

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
public sealed class MergeBooksHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<MergeBooks, BookMergeResult>
{
    public async Task<BookMergeResult> HandleAsync(MergeBooks command, CancellationToken ct = default)
    {
        if (command.WinnerId == command.LoserId)
        {
            return Failure("Winner and loser cannot be the same Book.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Works)
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == command.WinnerId, ct);
        var loser = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Works)
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == command.LoserId, ct);
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

        // Series membership is a Book field now (it moved Work→Book, TODO #56).
        // Pre-move it rode along on the loser's Works that get unioned below;
        // now it must be carried explicitly, else merging a series-less winner
        // with a series-bearing loser silently drops the book out of its series.
        if (winner.SeriesId is null && loser.SeriesId is not null)
        {
            winner.SeriesId = loser.SeriesId;
            winner.SeriesOrder = loser.SeriesOrder;
            winner.SeriesOrderDisplay = loser.SeriesOrderDisplay;
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

        // Soft-delete the loser: the row is kept as a tombstone for the
        // catalog snapshot's deletedIds[]. The merge has already moved
        // Editions to the winner and cleared Works/Tags on the loser, so
        // the husk row owns no aggregate. The global HasQueryFilter
        // hides the loser from all subsequent queries.
        loser.DeletedAt = DateTime.UtcNow;

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

    private static BookMergeResult Failure(string message) =>
        new(false, message, 0, 0, 0, 0, null, null);
}
