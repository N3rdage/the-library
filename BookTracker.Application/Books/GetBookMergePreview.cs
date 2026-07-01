using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model for the Book-merge preview page (/duplicates/merge/book/{a}/{b}).
// The merge write itself is the MergeBooks command. Relocated from the Web
// BookMergeService loader in PR6 — reads now live as query handlers in
// Application alongside the writes.
public sealed record GetBookMergePreview(int IdA, int IdB) : IQuery<BookMergeLoadResult>;

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

public sealed class GetBookMergePreviewHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetBookMergePreview, BookMergeLoadResult>
{
    public async Task<BookMergeLoadResult> HandleAsync(GetBookMergePreview query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (lowerId, higherId) = query.IdA < query.IdB ? (query.IdA, query.IdB) : (query.IdB, query.IdA);
        return new BookMergeLoadResult(
            await LoadDetailAsync(db, lowerId, ct),
            await LoadDetailAsync(db, higherId, ct));
    }

    private static async Task<BookMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var book = await db.Books
            .AsNoTracking()
            .Include(b => b.Editions)
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(b => b.BookWorks)   // for the per-book Order (headline pick)
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return null;

        var copyCount = await db.Copies.CountAsync(c => c.Edition.BookId == id, ct);

        // Cover pick: Book's own DefaultCoverArtUrl; fall back to the first
        // Edition's CoverUrl.
        var cover = !string.IsNullOrWhiteSpace(book.DefaultCoverArtUrl)
            ? book.DefaultCoverArtUrl
            : book.Editions.Select(e => e.CoverUrl).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        // Primary Work's authorship for the merge dialog headline — the Work the
        // user ordered first (BookWork.Order 0), matching the BookDetail headline
        // after a reorder. BookWorks carries only the ordering key; pick the work
        // itself from the already-loaded (authorship-hydrated) Works list.
        var primaryWorkId = book.BookWorks.OrderBy(bw => bw.Order).Select(bw => bw.WorkId).FirstOrDefault();
        var primaryWork = book.Works.FirstOrDefault(w => w.Id == primaryWorkId) ?? book.Works.FirstOrDefault();
        var firstAuthor = primaryWork is null ? null : WorkAuthorshipFormatter.Display(primaryWork);

        return new BookMergeDetail(
            book.Id, book.Title, firstAuthor,
            book.Category, book.Status, book.Rating, book.Notes, book.DateAdded,
            book.Editions.Count, copyCount,
            book.Works.Select(w => w.Title).OrderBy(t => t).ToList(),
            book.Tags.Select(t => t.Name).OrderBy(t => t).ToList(),
            cover);
    }
}
