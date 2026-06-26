using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IBookMergeService
{
    Task<BookMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default);
}

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

// Read-only loader for the Book-merge preview page. The merge write itself is
// the MergeBooks command in BookTracker.Application.Books (PR5). These reads
// stay here until the read-model relocation (PR6).
public class BookMergeService(IDbContextFactory<BookTrackerDbContext> dbFactory) : IBookMergeService
{
    public async Task<BookMergeLoadResult> LoadAsync(int idA, int idB, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (lowerId, higherId) = idA < idB ? (idA, idB) : (idB, idA);
        return new BookMergeLoadResult(
            await LoadDetailAsync(db, lowerId, ct),
            await LoadDetailAsync(db, higherId, ct));
    }

    private static async Task<BookMergeDetail?> LoadDetailAsync(BookTrackerDbContext db, int id, CancellationToken ct)
    {
        var book = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(b => b.Tags)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (book is null) return null;

        var copyCount = await db.Copies.CountAsync(c => c.Edition.BookId == id, ct);

        // Cover pick: Book's own DefaultCoverArtUrl; fall back to the first
        // Edition's CoverUrl.
        var cover = !string.IsNullOrWhiteSpace(book.DefaultCoverArtUrl)
            ? book.DefaultCoverArtUrl
            : book.Editions.Select(e => e.CoverUrl).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        // First Work's authorship for the merge dialog headline. Multi-Work
        // books show the first Work's display string; the user can drill into
        // BookDetail for the full picture.
        var primaryWork = book.Works.FirstOrDefault();
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
