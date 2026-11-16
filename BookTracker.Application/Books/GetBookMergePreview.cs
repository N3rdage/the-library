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
        // Projected (C5): the book scalars, its editions' cover URLs (in Edition
        // id order — the order the old Include graph gave the "first edition
        // cover" fallback), each work's per-book Order + title + contributors,
        // and the tag names. No entity graph is hydrated.
        var book = await db.Books
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.Category,
                b.Status,
                b.Rating,
                b.Notes,
                b.DateAdded,
                b.DefaultCoverArtUrl,
                EditionCoverUrls = b.Editions.OrderBy(e => e.Id).Select(e => e.CoverUrl).ToList(),
                Works = b.BookWorks.Select(bw => new
                {
                    bw.Order,
                    bw.WorkId,
                    bw.Work.Title,
                    // WorkAuthor key order (AuthorId, Role), as the Include graph
                    // handed the formatter's stable sort.
                    Contributors = bw.Work.WorkAuthors
                        .OrderBy(wa => wa.AuthorId)
                        .ThenBy(wa => wa.Role)
                        .Select(wa => new { wa.Author.Name, wa.Role, wa.Order })
                        .ToList(),
                }).ToList(),
                TagNames = b.Tags.Select(t => t.Name).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (book is null) return null;

        var copyCount = await db.Copies.CountAsync(c => c.Edition.BookId == id, ct);

        // Cover pick: Book's own DefaultCoverArtUrl; fall back to the first
        // Edition's CoverUrl.
        var cover = !string.IsNullOrWhiteSpace(book.DefaultCoverArtUrl)
            ? book.DefaultCoverArtUrl
            : book.EditionCoverUrls.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        // Primary Work's authorship for the merge dialog headline — the Work the
        // user ordered first (BookWork.Order 0), matching the BookDetail headline
        // after a reorder. WorkId breaks Order ties, as the BookWork key order did
        // for the old in-memory sort.
        var primaryWork = book.Works.OrderBy(w => w.Order).ThenBy(w => w.WorkId).FirstOrDefault();
        var firstAuthor = primaryWork is null
            ? null
            : WorkAuthorshipFormatter.Display(primaryWork.Contributors.Select(c => (c.Name, c.Role, c.Order)));

        return new BookMergeDetail(
            book.Id, book.Title, firstAuthor,
            book.Category, book.Status, book.Rating, book.Notes, book.DateAdded,
            book.EditionCoverUrls.Count, copyCount,
            book.Works.Select(w => w.Title).OrderBy(t => t).ToList(),
            book.TagNames.OrderBy(t => t).ToList(),
            cover);
    }
}
