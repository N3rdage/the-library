using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model for the book detail page (/books/{id}). Relocated from
// BookDetailViewModel.InitializeAsync in PR6b-3: the 6-Include aggregate read
// (AsSplitQuery) + the flat projection into the BookDetail display shape.
// AsNoTracking + DTO (C5). Returns null when the book is missing — including
// soft-deleted tombstones, which the global query filter hides.
public sealed record GetBookDetail(int BookId) : IQuery<BookDetail?>;

public record BookDetail(
    int Id,
    string Title,
    BookCategory Category,
    BookStatus Status,
    int Rating,
    string? Notes,
    string? CoverUrl,
    DateTime DateAdded,
    IReadOnlyList<WorkDetail> Works,
    IReadOnlyList<EditionDetail> Editions,
    IReadOnlyList<TagDetail> Tags);

public record WorkDetail(
    int Id,
    string Title,
    string? Subtitle,
    /// <summary>Formatted display string ("Preston" / "Preston &amp; Child" / "Preston, Child, Pendergast").</summary>
    string AuthorName,
    /// <summary>The lead author's Id — used for /authors/{id} deep-links from the BookDetail page.</summary>
    int LeadAuthorId,
    string FirstPublishedDisplay,
    IReadOnlyList<string> Genres,
    SeriesInfo? Series);

public record SeriesInfo(int Id, string Name, SeriesType Type, string? OrderLabel);

public record EditionDetail(
    int Id,
    string? Isbn,
    BookFormat Format,
    string FormatDisplay,
    string? Publisher,
    string DatePrintedDisplay,
    string? CoverUrl,
    bool IsUserSupplied,
    IReadOnlyList<CopyDetail> Copies,
    int? EditionNumber = null);

public record CopyDetail(int Id, BookCondition Condition, DateTime? DateAcquired, string? Notes);

public record TagDetail(int Id, string Name);

public sealed class GetBookDetailHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetBookDetail, BookDetail?>
{
    public async Task<BookDetail?> HandleAsync(GetBookDetail query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var book = await db.Books
            .AsNoTracking()
            .Include(b => b.Tags)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Copies)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Publisher)
            .Include(b => b.Works)
                .ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(b => b.Works)
                .ThenInclude(w => w.Genres)
            .Include(b => b.Works)
                .ThenInclude(w => w.Series)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == query.BookId, ct);

        if (book is null) return null;

        return new BookDetail(
            book.Id,
            book.Title,
            book.Category,
            book.Status,
            book.Rating,
            book.Notes,
            book.DefaultCoverArtUrl,
            book.DateAdded,
            book.Works
                .OrderBy(w => w.SeriesOrder ?? int.MaxValue)
                .ThenBy(w => w.Title)
                .Select(ToWorkDetail)
                .ToList(),
            book.Editions
                .OrderBy(e => e.DatePrinted ?? DateOnly.MaxValue)
                .ThenBy(e => e.Id)
                .Select(ToEditionDetail)
                .ToList(),
            book.Tags
                .OrderBy(t => t.Name)
                .Select(t => new TagDetail(t.Id, t.Name))
                .ToList());
    }

    private static WorkDetail ToWorkDetail(Work w) => new(
        w.Id,
        w.Title,
        w.Subtitle,
        // AuthorName carries the formatted multi-author display ("Preston & Child")
        // post-PR2; the field name stays so Razor consumers don't have to change.
        WorkAuthorshipFormatter.Display(w),
        // LeadAuthorId is the first WorkAuthor entry's Author — used by the
        // BookDetail '+author' link to deep-link into the /authors page.
        w.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author).OrderBy(wa => wa.Order).Select(wa => wa.AuthorId).FirstOrDefault(),
        PartialDateParser.Format(w.FirstPublishedDate, w.FirstPublishedDatePrecision),
        w.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList(),
        w.Series is null ? null : new SeriesInfo(w.Series.Id, w.Series.Name, w.Series.Type, SeriesOrderParser.Format(w.SeriesOrder, w.SeriesOrderDisplay)));

    private static EditionDetail ToEditionDetail(Edition e) => new(
        e.Id,
        e.Isbn,
        e.Format,
        e.Format.DisplayName(),
        e.Publisher?.Name,
        PartialDateParser.Format(e.DatePrinted, e.DatePrintedPrecision),
        e.CoverUrl,
        e.IsUserSupplied,
        e.Copies
            .OrderBy(c => c.DateAcquired ?? DateTime.MaxValue)
            .ThenBy(c => c.Id)
            .Select(c => new CopyDetail(c.Id, c.Condition, c.DateAcquired, c.Notes))
            .ToList(),
        e.EditionNumber);
}
