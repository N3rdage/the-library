using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

/// <summary>Merges the loser Work into the winner (both must credit the same
/// author set): reassigns the loser's Book memberships (dedup against Books that
/// already hold the winner), auto-fills empty winner fields (subtitle,
/// first-published date+precision) and unions genres, then deletes the loser
/// Work. Series is no longer a Work concept (it lives on the Book), so it isn't
/// carried across the merge.</summary>
public sealed record MergeWorks(int WinnerId, int LoserId) : ICommand<WorkMergeResult>;

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

public sealed class MergeWorksHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<MergeWorks, WorkMergeResult>
{
    public async Task<WorkMergeResult> HandleAsync(MergeWorks command, CancellationToken ct = default)
    {
        if (command.WinnerId == command.LoserId)
        {
            return Failure("Winner and loser cannot be the same Work.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Works
            .Include(w => w.Books)
            .Include(w => w.Genres)
            .Include(w => w.WorkAuthors)
            .FirstOrDefaultAsync(w => w.Id == command.WinnerId, ct);
        var loser = await db.Works
            .Include(w => w.Books).ThenInclude(b => b.BookWorks)  // AttachWork appends per book
            .Include(w => w.Genres)
            .Include(w => w.WorkAuthors)
            .FirstOrDefaultAsync(w => w.Id == command.LoserId, ct);
        if (winner is null || loser is null)
        {
            return Failure("One or both Works could not be found — they may already have been merged or deleted.");
        }

        // Author sets must match — two Works with different authorship can't
        // be merged into one (the result would conflate distinct creative
        // credits). User's expected to merge authors first if pen-name aliases
        // are involved. Distinct so multi-role rows (Tolkien as Author +
        // Illustrator) don't fail the SequenceEqual.
        var winnerAuthorIds = winner.WorkAuthors.Select(wa => wa.AuthorId).Distinct().OrderBy(id => id).ToList();
        var loserAuthorIds = loser.WorkAuthors.Select(wa => wa.AuthorId).Distinct().OrderBy(id => id).ToList();
        if (!winnerAuthorIds.SequenceEqual(loserAuthorIds))
        {
            return Failure("Works belong to different authors. Merge the authors first on /duplicates.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // ─── Auto-fill empty winner fields from loser ──────────────────
        // Only fills gaps — never overwrites a value winner already has.
        // Paired fields move together (date+precision) so both halves stay
        // consistent.
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
                book.AttachWork(winner);   // append winner after the book's existing works
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

    private static WorkMergeResult Failure(string message) =>
        new(false, message, 0, 0, 0, null, null);
}
