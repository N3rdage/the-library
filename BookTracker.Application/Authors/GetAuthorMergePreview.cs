using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Read-model for the Author-merge preview page (/duplicates/merge/author/{a}/{b}).
// The merge write itself is the MergeAuthors command; the compatibility rule the
// preview shows is shared with that handler via AuthorMergeCompatibility.
// Relocated from the Web AuthorMergeService loader in PR6.
public sealed record GetAuthorMergePreview(int IdA, int IdB) : IQuery<AuthorMergeLoadResult>;

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

public sealed class GetAuthorMergePreviewHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetAuthorMergePreview, AuthorMergeLoadResult>
{
    private const int SampleWorkLimit = 5;

    public async Task<AuthorMergeLoadResult> HandleAsync(GetAuthorMergePreview query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var (lowerId, higherId) = query.IdA < query.IdB ? (query.IdA, query.IdB) : (query.IdB, query.IdA);

        var lower = await LoadDetailAsync(db, lowerId, ct);
        var higher = await LoadDetailAsync(db, higherId, ct);

        string? incompatibility = null;
        if (lower is not null && higher is not null)
        {
            incompatibility = CheckCompatibility(lower, higher);
        }

        return new AuthorMergeLoadResult(lower, higher, incompatibility);
    }

    private static async Task<AuthorMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var author = await db.Authors
            .AsNoTracking()
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

        // Cover pick: prefer a Book that contains a single Work credited to this
        // author, fall back to any Book by the author (shared rule — BookCovers).
        var cover = await Books.BookCovers.PickAsync(
            db.Books.Where(b => b.Works.Any(w => w.Authors.Any(a => a.Id == id))), ct);

        return new AuthorMergeDetail(
            author.Id, author.Name,
            author.CanonicalAuthorId,
            author.CanonicalAuthor?.Name,
            workCount, aliasCount, sampleTitles,
            cover);
    }

    // Both authors must resolve to the same canonical (either directly or by one
    // being an alias of the other). The rule lives in AuthorMergeCompatibility so
    // the preview and the MergeAuthors write handler stay in lock-step.
    private static string? CheckCompatibility(AuthorMergeDetail a, AuthorMergeDetail b) =>
        AuthorMergeCompatibility.Check(
            a.Id, a.CanonicalAuthorId, a.Name,
            b.Id, b.CanonicalAuthorId, b.Name);
}
