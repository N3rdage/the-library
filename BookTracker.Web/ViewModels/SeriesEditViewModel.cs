using System.ComponentModel.DataAnnotations;
using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using BookTracker.Application.Formatting;

namespace BookTracker.Web.ViewModels;

// Series membership lives on Books after the cutover — the Book is installment
// N of a publication series, whether it holds a single Work or a whole
// short-story collection. The page lists each Book in the series (in order) so
// a series of collection books is managed at the book grain.
//
// Writes go through the Application layer (PR3b): create/edit/delete and the
// book-membership ops dispatch commands; reads (Initialize, SearchBooks) stay
// on the DbContext factory. The in-memory Books list is kept in sync after each
// mutation so the page doesn't reload.
public class SeriesEditViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IDispatcher dispatcher)
{
    public SeriesFormInput? Input { get; private set; }
    public List<SeriesBookRow> Books { get; private set; } = [];
    public bool NotFound { get; private set; }
    public bool Saving { get; private set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsNew { get; private set; }

    public bool ConfirmingDeleteSeries { get; set; }
    public bool Deleting { get; private set; }

    // Add book
    public bool ShowingAddBook { get; set; }
    public string BookSearchTerm { get; set; } = "";
    public List<BookSearchResult> BookSearchResults { get; private set; } = [];

    public void InitializeNew()
    {
        IsNew = true;
        Input = new SeriesFormInput();
    }

    public async Task InitializeAsync(int seriesId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var series = await db.Series
            .Include(s => s.Books).ThenInclude(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstOrDefaultAsync(s => s.Id == seriesId);

        if (series is null)
        {
            NotFound = true;
            return;
        }

        Input = new SeriesFormInput
        {
            Name = series.Name,
            Author = series.Author,
            Type = series.Type,
            ExpectedCount = series.ExpectedCount,
            Description = series.Description
        };

        Books = series.Books
            .OrderBy(b => b.SeriesOrder ?? int.MaxValue)
            .ThenBy(b => b.Title)
            .Select(b => new SeriesBookRow(
                b.Id,
                b.Title,
                BookAuthorDisplay(b),
                b.SeriesOrder,
                b.SeriesOrderDisplay))
            .ToList();
    }

    public async Task<int?> SaveAsync(int? seriesId)
    {
        Saving = true;
        // Clear both banners so a prior success + a new failure (or vice-versa)
        // can't render at once.
        ErrorMessage = null;
        SuccessMessage = null;
        // The aggregate normalises (trims name, nulls blank author/description,
        // drops ExpectedCount for a Collection), so pass the raw form values.
        try
        {
            if (IsNew)
            {
                return await dispatcher.Send(new CreateSeries(
                    Input!.Name!, Input.Author, Input.Type, Input.ExpectedCount, Input.Description));
            }
            else
            {
                await dispatcher.Send(new UpdateSeries(
                    seriesId!.Value, Input!.Name!, Input.Author, Input.Type, Input.ExpectedCount, Input.Description));
                SuccessMessage = "Series saved successfully.";
                return seriesId;
            }
        }
        catch (DomainRuleException ex)
        {
            // Duplicate (or blank) name — surface the friendly message and stay put.
            ErrorMessage = ex.Message;
            return null;
        }
        catch (NotFoundException)
        {
            // Series deleted out from under the editor between load and save.
            NotFound = true;
            return null;
        }
        finally
        {
            Saving = false;
        }
    }

    public async Task<bool> DeleteSeriesAsync(int seriesId)
    {
        Deleting = true;
        try
        {
            await dispatcher.Send(new DeleteSeries(seriesId)); // idempotent — no-op if already gone
            return true;
        }
        finally
        {
            Deleting = false;
        }
    }

    public async Task SearchBooksAsync()
    {
        if (string.IsNullOrWhiteSpace(BookSearchTerm))
        {
            BookSearchResults = [];
            return;
        }

        var term = BookSearchTerm.Trim();
        var currentBookIds = Books.Select(b => b.Id).ToHashSet();

        await using var db = await dbFactory.CreateDbContextAsync();
        // Load the works+authors graph for the (≤10) matches and build the author
        // string via BookAuthorDisplay — the SAME helper the in-series rows use —
        // so a book shows one consistent author in the search dropdown and in the
        // table once it's added.
        var matches = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Where(b => !currentBookIds.Contains(b.Id))
            .Where(b => b.Title.Contains(term) || b.Works.Any(w => w.Title.Contains(term) || w.Authors.Any(a => a.Name.Contains(term))))
            .OrderBy(b => b.Title)
            .Take(10)
            .ToListAsync();

        BookSearchResults = matches
            .Select(b => new BookSearchResult(b.Id, b.Title, BookAuthorDisplay(b), b.SeriesId))
            .ToList();
    }

    public async Task AddBookToSeriesAsync(int seriesId, int bookId)
    {
        if (Books.Any(b => b.Id == bookId)) return; // already shown (e.g. a double-click) — no dup row

        await dispatcher.Send(new AddBookToSeries(seriesId, bookId)); // handler assigns the next order

        // Reload the book to build its row (and read the order the handler set).
        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstOrDefaultAsync(b => b.Id == bookId);
        if (book is null) return;

        Books.Add(new SeriesBookRow(
            book.Id,
            book.Title,
            BookAuthorDisplay(book),
            book.SeriesOrder,
            book.SeriesOrderDisplay));
        BookSearchResults.RemoveAll(r => r.Id == bookId);
    }

    public async Task RemoveBookFromSeriesAsync(int bookId)
    {
        await dispatcher.Send(new RemoveBookFromSeries(bookId));
        Books.RemoveAll(b => b.Id == bookId);
    }

    public async Task UpdateBookOrderAsync(int bookId, string? rawOrder)
    {
        // Free-text so "4.5" interquels survive: the VM owns parsing into the
        // integer sort key + optional display override; the handler just stores them.
        var (order, display) = SeriesOrderParser.Parse(rawOrder);
        await dispatcher.Send(new SetBookSeriesOrder(bookId, order, display));

        var row = Books.FirstOrDefault(b => b.Id == bookId);
        if (row is not null)
        {
            var idx = Books.IndexOf(row);
            Books[idx] = row with { SeriesOrder = order, SeriesOrderDisplay = display };
        }
    }

    // A book's author display = distinct Author-role names across its Works,
    // lead-first within each Work. Mirrors the Library list's comma-join (the
    // " & " formatter is reserved for single-Work detail surfaces).
    private static string BookAuthorDisplay(Book book) =>
        string.Join(", ", book.Works
            .SelectMany(w => w.WorkAuthors
                .Where(wa => wa.Role == AuthorRole.Author)
                .OrderBy(wa => wa.Order)
                .Select(wa => wa.Author.Name))
            .Distinct());

    public record SeriesBookRow(int Id, string Title, string Author, int? SeriesOrder, string? SeriesOrderDisplay);
    public record BookSearchResult(int Id, string Title, string Author, int? CurrentSeriesId);

    public class SeriesFormInput
    {
        [Required, StringLength(300)]
        public string? Name { get; set; }

        [StringLength(200)]
        public string? Author { get; set; }

        public SeriesType Type { get; set; } = SeriesType.Series;

        public int? ExpectedCount { get; set; }

        public string? Description { get; set; }
    }
}
