using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

/// <summary>Merges the loser author into the winner (both must resolve to the
/// same canonical — see <see cref="AuthorMergeCompatibility"/>): reassigns every
/// WorkAuthor credit (dedup by Work+Role so distinct roles survive), re-points
/// the loser's external aliases at the winner, promotes the winner to canonical
/// if it was an alias of the loser, then deletes the loser. Lifted verbatim from
/// the old AuthorMergeService write path (PR5, back-end refactor).</summary>
public sealed record MergeAuthors(int WinnerId, int LoserId) : ICommand<AuthorMergeResult>;

public record AuthorMergeResult(
    bool Success,
    string? ErrorMessage,
    int WorksReassigned,
    int AliasesReassigned,
    bool WinnerPromotedToCanonical,
    string? WinnerName,
    string? LoserName);

public sealed class MergeAuthorsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<MergeAuthors, AuthorMergeResult>
{
    public async Task<AuthorMergeResult> HandleAsync(MergeAuthors command, CancellationToken ct = default)
    {
        if (command.WinnerId == command.LoserId)
        {
            return Failure("Winner and loser cannot be the same author.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Authors.FirstOrDefaultAsync(a => a.Id == command.WinnerId, ct);
        var loser = await db.Authors.FirstOrDefaultAsync(a => a.Id == command.LoserId, ct);
        if (winner is null || loser is null)
        {
            return Failure("One or both authors could not be found — they may already have been merged or deleted.");
        }

        var incompatibility = AuthorMergeCompatibility.Check(
            winner.Id, winner.CanonicalAuthorId, winner.Name,
            loser.Id, loser.CanonicalAuthorId, loser.Name);
        if (incompatibility is not null)
        {
            return Failure(incompatibility);
        }

        // Case 4 in the compatibility matrix: winner is an alias of loser.
        // Loser is about to be deleted, so winner must be promoted to canonical
        // before the delete — otherwise winner's CanonicalAuthorId would dangle.
        var winnerWasAliasOfLoser = winner.CanonicalAuthorId == loser.Id;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (winnerWasAliasOfLoser)
        {
            winner.CanonicalAuthorId = null;
        }

        // Reassign every WorkAuthor row from loser to winner. Composite PK
        // (WorkId, AuthorId, Role) means we can't UPDATE the AuthorId column
        // — delete + add. Dedup by (WorkId, Role): if winner is already
        // credited on the same Work in the same Role, drop the loser row;
        // otherwise re-add as winner so distinct roles survive (e.g. winner
        // is Author of Work X, loser is Translator of Work X — both should
        // exist post-merge under winner).
        var loserWorkAuthors = await db.WorkAuthors
            .Where(wa => wa.AuthorId == loser.Id)
            .ToListAsync(ct);

        var winnerCreditedByWorkAndRole = (await db.WorkAuthors
            .Where(wa => wa.AuthorId == winner.Id)
            .Select(wa => new { wa.WorkId, wa.Role })
            .ToListAsync(ct))
            .Select(x => (x.WorkId, x.Role))
            .ToHashSet();

        foreach (var wa in loserWorkAuthors)
        {
            db.WorkAuthors.Remove(wa);
            if (!winnerCreditedByWorkAndRole.Contains((wa.WorkId, wa.Role)))
            {
                db.WorkAuthors.Add(new WorkAuthor
                {
                    WorkId = wa.WorkId,
                    AuthorId = winner.Id,
                    Order = wa.Order,
                    Role = wa.Role,
                });
                winnerCreditedByWorkAndRole.Add((wa.WorkId, wa.Role));
            }
        }
        var worksReassignedCount = loserWorkAuthors.Count;

        // External aliases of the loser become aliases of the winner. Must
        // exclude the winner explicitly — if winner was an alias of loser its
        // CanonicalAuthorId was nulled in memory above, but that change isn't
        // saved yet so this DB query still sees the old value; without the
        // guard the winner would be re-pointed at itself.
        var aliases = await db.Authors
            .Where(a => a.CanonicalAuthorId == loser.Id && a.Id != winner.Id)
            .ToListAsync(ct);
        foreach (var a in aliases)
        {
            a.CanonicalAuthorId = winner.Id;
        }

        // Clear any dismissed-dup rows referencing the loser — they're about to
        // become orphans anyway. DetectAllAsync would sweep them on the next
        // run, but removing them here keeps the UI tidy immediately.
        var staleIgnores = await db.IgnoredDuplicates
            .Where(d => d.EntityType == DuplicateEntityType.Author
                    && (d.LowerId == loser.Id || d.HigherId == loser.Id))
            .ToListAsync(ct);
        db.IgnoredDuplicates.RemoveRange(staleIgnores);

        db.Authors.Remove(loser);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new AuthorMergeResult(
            Success: true,
            ErrorMessage: null,
            WorksReassigned: worksReassignedCount,
            AliasesReassigned: aliases.Count,
            WinnerPromotedToCanonical: winnerWasAliasOfLoser,
            WinnerName: winner.Name,
            LoserName: loser.Name);
    }

    private static AuthorMergeResult Failure(string message) =>
        new(false, message, 0, 0, false, null, null);
}
