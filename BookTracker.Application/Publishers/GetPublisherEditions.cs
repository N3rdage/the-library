using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Publishers;

// Read-model for a publisher row's lazy-loaded edition drill-down on /publishers.
// Relocated from PublisherListViewModel.LoadDetailAsync in PR6b-2. Returns the
// editions referencing this publisher, ordered by book title then print date.
public sealed record GetPublisherEditions(int PublisherId) : IQuery<PublisherDetail>;

public record PublisherDetail(IReadOnlyList<EditionRow> Editions)
{
    public static PublisherDetail Empty => new([]);
}

public record EditionRow(
    int Id,
    int BookId,
    string BookTitle,
    string? Isbn,
    BookFormat Format,
    DateOnly? DatePrinted,
    string? CoverUrl,
    int CopyCount);

public sealed class GetPublisherEditionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetPublisherEditions, PublisherDetail>
{
    public async Task<PublisherDetail> HandleAsync(GetPublisherEditions query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var editions = await db.Editions
            .AsNoTracking()
            .Where(e => e.PublisherId == query.PublisherId)
            .Include(e => e.Book)
            .Include(e => e.Copies)
            .OrderBy(e => e.Book.Title)
            .ThenBy(e => e.DatePrinted)
            .Select(e => new EditionRow(
                e.Id,
                e.BookId,
                e.Book.Title,
                e.Isbn,
                e.Format,
                e.DatePrinted,
                e.CoverUrl,
                e.Copies.Count))
            .ToListAsync(ct);

        return new PublisherDetail(editions);
    }
}
