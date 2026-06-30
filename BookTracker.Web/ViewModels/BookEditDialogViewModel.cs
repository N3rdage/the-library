using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Dialog-scoped VM for "Edit book details" on the View page. Covers
// Book-level fields that sit apart from the Work (title, category,
// cover URL) plus series membership + order — the Book is installment N
// of a publication series. Notes have inline auto-save on the View page
// and are deliberately absent here. Genres live on the Work and are
// edited via WorkEditDialogViewModel.
public class BookEditDialogViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IDispatcher dispatcher)
{
    public bool NotFound { get; private set; }
    public int BookId { get; private set; }

    public string Title { get; set; } = "";
    public BookCategory Category { get; set; }
    public string? CoverUrl { get; set; }

    public int? SelectedSeriesId { get; set; }
    // Free-text so the user can enter "4.5" interquels / "1A" hierarchical
    // positions — parsed into (SeriesOrder int sort key, SeriesOrderDisplay
    // override) on save via SeriesOrderParser.
    public string? SeriesOrderInput { get; set; }

    public List<SeriesOption> AvailableSeries { get; private set; } = [];

    public async Task InitializeAsync(int bookId)
    {
        BookId = bookId;
        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(bookId);
        if (book is null) { NotFound = true; return; }

        Title = book.Title;
        Category = book.Category;
        CoverUrl = book.DefaultCoverArtUrl;
        SelectedSeriesId = book.SeriesId;
        // Only surface an order when the book is actually in a series. A series
        // delete SET NULLs SeriesId but leaves SeriesOrder behind, so a stale
        // order must not pre-fill the field and silently ride into the next
        // series the user picks.
        SeriesOrderInput = book.SeriesId is null
            ? null
            : SeriesOrderParser.Format(book.SeriesOrder, book.SeriesOrderDisplay);

        AvailableSeries = await db.Series
            .OrderBy(s => s.Name)
            .Select(s => new SeriesOption(s.Id, s.Name, s.Type))
            .ToListAsync();
    }

    public async Task SaveAsync()
    {
        if (NotFound || string.IsNullOrWhiteSpace(Title)) return;

        int? seriesOrder = null;
        string? seriesOrderDisplay = null;
        if (SelectedSeriesId.HasValue)
            (seriesOrder, seriesOrderDisplay) = SeriesOrderParser.Parse(SeriesOrderInput);

        try
        {
            await dispatcher.Send(new UpdateBookDetails(
                BookId, Title, Category, CoverUrl,
                SelectedSeriesId, seriesOrder, seriesOrderDisplay));
        }
        catch (NotFoundException)
        {
            // Book deleted between opening the dialog and saving — no-op, matching
            // the old FindAsync-returns-null path.
        }
    }

    public record SeriesOption(int Id, string Name, SeriesType Type);
}
