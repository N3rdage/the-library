using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model for the Library flat book list — filtered, sorted, paginated.
// Relocated from BookListViewModel.LoadBooksAsync in PR6b-4. Returns the
// requested page's items plus the totals and the (possibly clamped) effective
// page, so the VM can write a corrected page back to the URL.
public sealed record GetLibraryBooks(LibraryFilter Filter, int Page) : IQuery<LibraryBooksResult>
{
    public const int PageSize = 20;
}

public record LibraryBooksResult(
    IReadOnlyList<BookListItem> Books, int TotalCount, int TotalPages, int Page);

public sealed class GetLibraryBooksHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetLibraryBooks, LibraryBooksResult>
{
    public async Task<LibraryBooksResult> HandleAsync(GetLibraryBooks query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var f = query.Filter;
        var filtered = LibraryBookQuery.Filtered(db, f);

        var totalCount = await filtered.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)GetLibraryBooks.PageSize));
        var page = query.Page > totalPages ? totalPages : query.Page;

        // Filtering to a single series sorts by that series' reading order rather
        // than DateAdded — reading order is the whole point of looking at a
        // series (the replacement for the retired Series grouping, TODO #53c).
        // Every other view keeps newest-first.
        IQueryable<Book> ordered = LibraryFilter.IsSpecificSeries(f.SeriesId)
            ? filtered
                .OrderBy(b => b.Works
                    .Where(w => w.SeriesId == f.SeriesId)
                    .Min(w => (int?)w.SeriesOrder) ?? int.MaxValue)
                .ThenBy(b => b.Title)
            : filtered.OrderByDescending(b => b.DateAdded);

        var raw = await ordered
            .Skip((page - 1) * GetLibraryBooks.PageSize)
            .Take(GetLibraryBooks.PageSize)
            .ToListAsync(ct);

        return new LibraryBooksResult(
            raw.Select(ToBookListItem).ToList(), totalCount, totalPages, page);
    }

    private static BookListItem ToBookListItem(Book b) => new(
        b.Id,
        b.Title,
        // Subtitle only renders for single-Work books — for collections the
        // inner-Work subtitle would be an arbitrary story's, which reads as noise.
        b.Works.Count == 1 ? b.Works.First().Subtitle : null,
        // Comma-join unique author names across all Works. List views stay
        // uniform; the " & " formatter is reserved for single-Work surfaces.
        string.Join(", ", b.Works.SelectMany(w => w.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author).OrderBy(wa => wa.Order).Select(wa => wa.Author.Name)).Distinct()),
        b.DefaultCoverArtUrl,
        b.Status,
        b.Rating,
        b.Works.Count,
        b.Works.SelectMany(w => w.Genres).Select(g => g.Name).Distinct().ToList(),
        b.Tags.Select(t => t.Name).ToList());
}
