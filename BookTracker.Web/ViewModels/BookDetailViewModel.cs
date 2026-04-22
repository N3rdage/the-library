using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Read-only view model for the book detail page (View page, /books/{id}).
// Deliberately flat display shape — no form inputs, no edit state — so the
// page can focus on browsing. Edit surfaces will land in later PRs via
// inline auto-save (rating/status/notes/tags) and modal dialogs
// (work/edition/copy edits).
public class BookDetailViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool NotFound { get; private set; }
    public BookDetail? Book { get; private set; }

    public bool IsSingleWork => Book is not null && Book.Works.Count == 1;
    public int TotalEditions => Book?.Editions.Count ?? 0;
    public int TotalCopies => Book?.Editions.Sum(e => e.Copies.Count) ?? 0;

    public async Task InitializeAsync(int bookId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var book = await db.Books
            .Include(b => b.Tags)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Copies)
            .Include(b => b.Editions)
                .ThenInclude(e => e.Publisher)
            .Include(b => b.Works)
                .ThenInclude(w => w.Author)
            .Include(b => b.Works)
                .ThenInclude(w => w.Genres)
            .Include(b => b.Works)
                .ThenInclude(w => w.Series)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null)
        {
            NotFound = true;
            return;
        }

        Book = new BookDetail(
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
        w.Author.Name,
        w.AuthorId,
        PartialDateParser.Format(w.FirstPublishedDate, w.FirstPublishedDatePrecision),
        w.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList(),
        w.Series is null ? null : new SeriesInfo(w.Series.Id, w.Series.Name, w.Series.Type, w.SeriesOrder));

    private static EditionDetail ToEditionDetail(Edition e) => new(
        e.Id,
        e.Isbn,
        e.Format,
        e.Format.DisplayName(),
        e.Publisher?.Name,
        PartialDateParser.Format(e.DatePrinted, e.DatePrintedPrecision),
        e.CoverUrl,
        e.Copies
            .OrderBy(c => c.DateAcquired ?? DateTime.MaxValue)
            .ThenBy(c => c.Id)
            .Select(c => new CopyDetail(c.Id, c.Condition, c.DateAcquired, c.Notes))
            .ToList());

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
        string AuthorName,
        int AuthorId,
        string FirstPublishedDisplay,
        IReadOnlyList<string> Genres,
        SeriesInfo? Series);

    public record SeriesInfo(int Id, string Name, SeriesType Type, int? Order);

    public record EditionDetail(
        int Id,
        string? Isbn,
        BookFormat Format,
        string FormatDisplay,
        string? Publisher,
        string DatePrintedDisplay,
        string? CoverUrl,
        IReadOnlyList<CopyDetail> Copies);

    public record CopyDetail(int Id, BookCondition Condition, DateTime? DateAcquired, string? Notes);

    public record TagDetail(int Id, string Name);
}
