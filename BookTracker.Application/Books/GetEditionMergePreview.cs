using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model for the Edition-merge preview page (/duplicates/merge/edition/{a}/{b}).
// The merge write itself is the MergeEditions command. Relocated from the Web
// EditionMergeService loader in PR6.
public sealed record GetEditionMergePreview(int IdA, int IdB) : IQuery<EditionMergeLoadResult>;

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

public sealed class GetEditionMergePreviewHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetEditionMergePreview, EditionMergeLoadResult>
{
    public async Task<EditionMergeLoadResult> HandleAsync(GetEditionMergePreview query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var (lowerId, higherId) = query.IdA < query.IdB ? (query.IdA, query.IdB) : (query.IdB, query.IdA);

        var lower = await LoadDetailAsync(db, lowerId, ct);
        var higher = await LoadDetailAsync(db, higherId, ct);

        string? incompatibility = null;
        if (lower is not null && higher is not null && lower.BookId != higher.BookId)
        {
            incompatibility = $"These Editions belong to different Books (\"{lower.BookTitle}\" vs \"{higher.BookTitle}\"). If the Books themselves are duplicates, merge them first on /duplicates.";
        }

        return new EditionMergeLoadResult(lower, higher, incompatibility);
    }

    private static async Task<EditionMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var edition = await db.Editions
            .AsNoTracking()
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
}
