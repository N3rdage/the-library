using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IAuthorMergeService
{
    Task<AuthorMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default);
    Task<AuthorMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default);
}

public record AuthorMergeLoadResult(
    AuthorMergeDetail? Lower,
    AuthorMergeDetail? Higher,
    string? IncompatibilityReason);

public record AuthorMergeDetail(
    int Id,
    string Name,
    int? CanonicalAuthorId,
    string? CanonicalName,
    int WorkCount,
    int AliasCount,
    IReadOnlyList<string> SampleWorkTitles,
    // Best-effort cover URL — picks the DefaultCoverArtUrl of a Book
    // attached to any of this Author's Works, preferring a single-Work
    // Book over a compendium.
    string? CoverArtUrl);

public record AuthorMergeResult(
    bool Success,
    string? ErrorMessage,
    int WorksReassigned,
    int AliasesReassigned,
    bool WinnerPromotedToCanonical,
    string? WinnerName,
    string? LoserName);

public class AuthorMergeService(IDbContextFactory<BookTrackerDbContext> dbFactory) : IAuthorMergeService
{
    private const int SampleWorkLimit = 5;

    public async Task<AuthorMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var (lowerId, higherId) = idA < idB ? (idA, idB) : (idB, idA);

        var lower = await LoadDetailAsync(db, lowerId, ct);
        var higher = await LoadDetailAsync(db, higherId, ct);

        string? incompatibility = null;
        if (lower is not null && higher is not null)
        {
            incompatibility = CheckCompatibility(lower, higher);
        }

        return new AuthorMergeLoadResult(lower, higher, incompatibility);
    }

    public async Task<AuthorMergeResult> MergeAsync(int winnerId, int loserId, CancellationToken ct = default)
    {
        if (winnerId == loserId)
        {
            return Failure("Winner and loser cannot be the same author.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var winner = await db.Authors.FirstOrDefaultAsync(a => a.Id == winnerId, ct);
        var loser = await db.Authors.FirstOrDefaultAsync(a => a.Id == loserId, ct);
        if (winner is null || loser is null)
        {
            return Failure("One or both authors could not be found — they may already have been merged or deleted.");
        }

        var winnerDetail = MinimalDetail(winner);
        var loserDetail = MinimalDetail(loser);
        var incompatibility = CheckCompatibility(winnerDetail, loserDetail);
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
        // (WorkId, AuthorId) means we can'\''t UPDATE the AuthorId column —
        // delete + add. If winner is already credited on the same Work, drop
        // the loser row to avoid a duplicate composite key (the merge
        // collapses both into one credit).
        var loserWorkAuthors = await db.WorkAuthors
            .Where(wa => wa.AuthorId == loser.Id)
            .ToListAsync(ct);

        var winnerCreditedWorkIds = (await db.WorkAuthors
            .Where(wa => wa.AuthorId == winner.Id)
            .Select(wa => wa.WorkId)
            .ToListAsync(ct))
            .ToHashSet();

        foreach (var wa in loserWorkAuthors)
        {
            db.WorkAuthors.Remove(wa);
            if (!winnerCreditedWorkIds.Contains(wa.WorkId))
            {
                db.WorkAuthors.Add(new WorkAuthor
                {
                    WorkId = wa.WorkId,
                    AuthorId = winner.Id,
                    Order = wa.Order,
                });
                winnerCreditedWorkIds.Add(wa.WorkId);
            }
        }
        var worksReassignedCount = loserWorkAuthors.Count;

        // External aliases of the loser become aliases of the winner. Must
        // exclude the winner explicitly — if winner was an alias of loser its
        // CanonicalAuthorId was nulled in memory above, but the pending
        // change isn't visible to this query via the InMemory provider, so
        // the winner would otherwise be re-pointed at itself.
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

    private static async Task<AuthorMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var author = await db.Authors
            .Include(a => a.CanonicalAuthor)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (author is null) return null;

        var workCount = await db.Works.CountAsync(w => w.Authors.Any(a => a.Id == id), ct);
        var aliasCount = await db.Authors.CountAsync(a => a.CanonicalAuthorId == id, ct);
        var sampleTitles = await db.Works
            .Where(w => w.Authors.Any(a => a.Id == id))
            .OrderBy(w => w.Title)
            .Take(SampleWorkLimit)
            .Select(w => w.Title)
            .ToListAsync(ct);

        // Cover pick: prefer a Book that contains a single Work credited to
        // this author (its cover faithfully represents one of the author's
        // Works); fall back to any Book by the author.
        var singleWorkBookCover = await db.Books
            .Where(b => b.Works.Any(w => w.Authors.Any(a => a.Id == id)) && b.Works.Count == 1)
            .Select(b => b.DefaultCoverArtUrl)
            .FirstOrDefaultAsync(ct);
        var cover = singleWorkBookCover is null
            ? await db.Books
                .Where(b => b.Works.Any(w => w.Authors.Any(a => a.Id == id)))
                .Select(b => b.DefaultCoverArtUrl)
                .FirstOrDefaultAsync(ct)
            : singleWorkBookCover;

        return new AuthorMergeDetail(
            author.Id, author.Name,
            author.CanonicalAuthorId,
            author.CanonicalAuthor?.Name,
            workCount, aliasCount, sampleTitles,
            cover);
    }

    private static AuthorMergeDetail MinimalDetail(Author a) =>
        new(a.Id, a.Name, a.CanonicalAuthorId, null, 0, 0, [], null);

    // Compatibility matrix. Both authors must resolve to the same canonical
    // (either directly or by one being an alias of the other). Anything else
    // is refused — the user resolves alias relationships on /authors first.
    private static string? CheckCompatibility(AuthorMergeDetail a, AuthorMergeDetail b)
    {
        if (a.CanonicalAuthorId == b.CanonicalAuthorId) return null;
        if (a.CanonicalAuthorId == b.Id) return null;
        if (b.CanonicalAuthorId == a.Id) return null;

        return $"\"{a.Name}\" and \"{b.Name}\" resolve to different canonical authors, so merging them directly would silently drop the pen-name relationship. Resolve the aliases on /authors first (promote one to canonical, or alias both to the same root), then come back to merge.";
    }

    private static AuthorMergeResult Failure(string message) =>
        new(false, message, 0, 0, false, null, null);
}
