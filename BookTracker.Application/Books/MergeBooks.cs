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
            .Include(b => b.BookWorks)   // for AttachWork's append-order
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == command.WinnerId, ct);
        var loser = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Works)
            // Each Work's own BookWorks so RemoveFrom can ref-count it across
            // its other books.
            .Include(b => b.BookWorks).ThenInclude(bw => bw.Work).ThenInclude(w => w.BookWorks)
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
            winner.UpdateNotes(loser.Notes);
            fieldsAutoFilled++;
        }

        if (string.IsNullOrWhiteSpace(winner.DefaultCoverArtUrl) && !string.IsNullOrWhiteSpace(loser.DefaultCoverArtUrl))
        {
            winner.SetCover(loser.DefaultCoverArtUrl);
            fieldsAutoFilled++;
        }

        if (winner.Rating == 0 && loser.Rating > 0)
        {
            // Rate rejects values outside 0–5. A stored rating is always in range
            // (Rate and MarkRead guard every post-creation write; the Add form
            // validates the rating Book.Create stores), so the guard cannot fire
            // on a loaded loser.
            winner.Rate(loser.Rating);
            fieldsAutoFilled++;
        }

        // Series membership is a Book field now (it moved Work→Book, TODO #56).
        // Pre-move it rode along on the loser's Works that get unioned below;
        // now it must be carried explicitly, else merging a series-less winner
        // with a series-bearing loser silently drops the book out of its series.
        if (winner.SeriesId is null && loser.SeriesId is int loserSeriesId)
        {
            winner.AssignToSeries(loserSeriesId, loser.SeriesOrder, loser.SeriesOrderDisplay);
            fieldsAutoFilled++;
        }

        // ─── Union Works ───────────────────────────────────────────────
        var winnerWorkIds = winner.Works.Select(w => w.Id).ToHashSet();
        var worksUnioned = 0;
        foreach (var work in loser.Works.Where(w => !winnerWorkIds.Contains(w.Id)).ToList())
        {
            winner.AttachWork(work);   // append after winner's existing works
            winnerWorkIds.Add(work.Id);
            worksUnioned++;
        }

        // Detach every Work from the loser through the ref-count lifecycle; a
        // Work in no book now is orphaned and deleted. Every loser Work is on
        // the winner by this point, so none is orphaned in practice.
        var orphaned = new List<Work>();
        foreach (var work in loser.BookWorks.Select(bw => bw.Work).ToList())
            if (work.RemoveFrom(loser)) orphaned.Add(work);
        if (orphaned.Count > 0) db.Works.RemoveRange(orphaned);

        // ─── Union Tags ────────────────────────────────────────────────
        var tagsUnioned = winner.AdoptTagsFrom(loser);

        // ─── Reassign Editions ─────────────────────────────────────────
        // Editions (and their Copies, which belong to Edition not Book)
        // move as a unit by flipping the BookId FK.
        var editionsReassigned = loser.MoveEditionsTo(winner);

        var staleIgnores = await db.IgnoredDuplicates
            .Where(d => d.EntityType == DuplicateEntityType.Book
                    && (d.LowerId == loser.Id || d.HigherId == loser.Id))
            .ToListAsync(ct);
        db.IgnoredDuplicates.RemoveRange(staleIgnores);

        // Soft-delete the loser: the row is kept as a tombstone for the
        // catalog snapshot's deletedIds[]. The merge has already moved
        // Editions to the winner, detached its Works and cleared its Tags, so
        // SoftDelete's own Editions/Tags clear finds both collections empty
        // and the husk row owns no aggregate. The global HasQueryFilter
        // hides the loser from all subsequent queries.
        loser.SoftDelete();

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
