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
    IReadOnlyList<TagDetail> Tags,
    // Series membership lives on the Book — the Book is installment N of a
    // publication series. Null when the book isn't part of one.
    SeriesInfo? Series);

public record WorkDetail(
    int Id,
    string Title,
    string? Subtitle,
    /// <summary>Formatted display string ("Preston" / "Preston &amp; Child" / "Preston, Child, Pendergast").</summary>
    string AuthorName,
    /// <summary>The lead author's Id — used for /authors/{id} deep-links from the BookDetail page.</summary>
    int LeadAuthorId,
    string FirstPublishedDisplay,
    IReadOnlyList<string> Genres);

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

        // Project the display shape directly — pull only the scalars and the
        // small contributor / genre / copy lists the records need, rather than
        // hydrating the whole Book→Editions→Copies / Works→{WorkAuthors,Genres,
        // Series} / Tags graph (the old 6-Include AsSplitQuery read). The C#
        // formatters (multi-author display, partial dates, format / series
        // labels) run over the materialised projection below.
        var book = await db.Books
            .AsNoTracking()
            .Where(b => b.Id == query.BookId)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.Category,
                b.Status,
                b.Rating,
                b.Notes,
                b.DefaultCoverArtUrl,
                b.DateAdded,
                b.SeriesOrder,
                b.SeriesOrderDisplay,
                SeriesId = b.SeriesId,
                SeriesName = b.Series == null ? null : b.Series.Name,
                SeriesType = b.Series == null ? (SeriesType?)null : b.Series.Type,
                Works = b.Works.Select(w => new
                {
                    w.Id,
                    w.Title,
                    w.Subtitle,
                    w.FirstPublishedDate,
                    w.FirstPublishedDatePrecision,
                    Contributors = w.WorkAuthors.Select(wa => new
                    {
                        wa.AuthorId,
                        wa.Author.Name,
                        wa.Role,
                        wa.Order,
                    }).ToList(),
                    Genres = w.Genres.Select(g => g.Name).ToList(),
                }).ToList(),
                Editions = b.Editions.Select(e => new
                {
                    e.Id,
                    e.Isbn,
                    e.Format,
                    PublisherName = e.Publisher == null ? null : e.Publisher.Name,
                    e.DatePrinted,
                    e.DatePrintedPrecision,
                    e.CoverUrl,
                    e.IsUserSupplied,
                    e.EditionNumber,
                    Copies = e.Copies.Select(c => new
                    {
                        c.Id,
                        c.Condition,
                        c.DateAcquired,
                        c.Notes,
                    }).ToList(),
                }).ToList(),
                Tags = b.Tags.Select(t => new { t.Id, t.Name }).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (book is null) return null;

        var works = book.Works
            .OrderBy(w => w.Title)
            .Select(w => new WorkDetail(
                w.Id,
                w.Title,
                w.Subtitle,
                // AuthorName carries the formatted multi-author display
                // ("Preston & Child"); the (Name, Role, Order) overload owns the
                // canonical contributor sort so it isn't re-spelled here.
                WorkAuthorshipFormatter.Display(
                    w.Contributors.Select(c => (c.Name, c.Role, c.Order))),
                // LeadAuthorId — first Author-role contributor — for the
                // BookDetail '+author' deep-link into /authors.
                w.Contributors
                    .Where(c => c.Role == AuthorRole.Author)
                    .OrderBy(c => c.Order)
                    .Select(c => c.AuthorId)
                    .FirstOrDefault(),
                PartialDateParser.Format(w.FirstPublishedDate, w.FirstPublishedDatePrecision),
                w.Genres.OrderBy(g => g).ToList()))
            .ToList();

        var editions = book.Editions
            .OrderBy(e => e.DatePrinted ?? DateOnly.MaxValue)
            .ThenBy(e => e.Id)
            .Select(e => new EditionDetail(
                e.Id,
                e.Isbn,
                e.Format,
                e.Format.DisplayName(),
                e.PublisherName,
                PartialDateParser.Format(e.DatePrinted, e.DatePrintedPrecision),
                e.CoverUrl,
                e.IsUserSupplied,
                e.Copies
                    .OrderBy(c => c.DateAcquired ?? DateTime.MaxValue)
                    .ThenBy(c => c.Id)
                    .Select(c => new CopyDetail(c.Id, c.Condition, c.DateAcquired, c.Notes))
                    .ToList(),
                e.EditionNumber))
            .ToList();

        var tags = book.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagDetail(t.Id, t.Name))
            .ToList();

        var series = book.SeriesId is null
            ? null
            : new SeriesInfo(book.SeriesId.Value, book.SeriesName!, book.SeriesType!.Value,
                SeriesOrderParser.Format(book.SeriesOrder, book.SeriesOrderDisplay));

        return new BookDetail(
            book.Id, book.Title, book.Category, book.Status, book.Rating,
            book.Notes, book.DefaultCoverArtUrl, book.DateAdded, works, editions, tags, series);
    }
}
