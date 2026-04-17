using System.ComponentModel.DataAnnotations;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class SeriesEditViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public SeriesFormInput? Input { get; private set; }
    public List<SeriesBookRow> Books { get; private set; } = [];
    public bool NotFound { get; private set; }
    public bool Saving { get; private set; }
    public string? SuccessMessage { get; set; }
    public bool IsNew { get; private set; }

    // Book deletion
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
            .Include(s => s.Books)
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
            .Select(b => new SeriesBookRow(b.Id, b.Title, b.Author, b.SeriesOrder))
            .ToList();
    }

    public async Task<int?> SaveAsync(int? seriesId)
    {
        Saving = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (IsNew)
            {
                var series = new Series
                {
                    Name = Input!.Name!.Trim(),
                    Author = string.IsNullOrWhiteSpace(Input.Author) ? null : Input.Author.Trim(),
                    Type = Input.Type,
                    ExpectedCount = Input.Type == SeriesType.Series ? Input.ExpectedCount : null,
                    Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim()
                };

                db.Series.Add(series);
                await db.SaveChangesAsync();
                return series.Id;
            }
            else
            {
                var series = await db.Series.FindAsync(seriesId!.Value);
                if (series is null) { NotFound = true; return null; }

                series.Name = Input!.Name!.Trim();
                series.Author = string.IsNullOrWhiteSpace(Input.Author) ? null : Input.Author.Trim();
                series.Type = Input.Type;
                series.ExpectedCount = Input.Type == SeriesType.Series ? Input.ExpectedCount : null;
                series.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();

                await db.SaveChangesAsync();
                SuccessMessage = "Series saved successfully.";
                return seriesId;
            }
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
            await using var db = await dbFactory.CreateDbContextAsync();
            var series = await db.Series.FindAsync(seriesId);
            if (series is not null)
            {
                db.Series.Remove(series);
                await db.SaveChangesAsync();
            }
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
        BookSearchResults = await db.Books
            .Where(b => !currentBookIds.Contains(b.Id))
            .Where(b => b.Title.Contains(term) || b.Author.Contains(term))
            .OrderBy(b => b.Title)
            .Take(10)
            .Select(b => new BookSearchResult(b.Id, b.Title, b.Author, b.SeriesId))
            .ToListAsync();
    }

    public async Task AddBookToSeriesAsync(int seriesId, int bookId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(bookId);
        if (book is null) return;

        var nextOrder = Books.Count > 0 ? Books.Max(b => b.SeriesOrder ?? 0) + 1 : 1;

        book.SeriesId = seriesId;
        book.SeriesOrder = nextOrder;
        await db.SaveChangesAsync();

        Books.Add(new SeriesBookRow(book.Id, book.Title, book.Author, nextOrder));
        BookSearchResults.RemoveAll(r => r.Id == bookId);
    }

    public async Task RemoveBookFromSeriesAsync(int bookId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(bookId);
        if (book is not null)
        {
            book.SeriesId = null;
            book.SeriesOrder = null;
            await db.SaveChangesAsync();
        }
        Books.RemoveAll(b => b.Id == bookId);
    }

    public async Task UpdateBookOrderAsync(int bookId, int? newOrder)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(bookId);
        if (book is not null)
        {
            book.SeriesOrder = newOrder;
            await db.SaveChangesAsync();
        }

        var row = Books.FirstOrDefault(b => b.Id == bookId);
        if (row is not null)
        {
            var idx = Books.IndexOf(row);
            Books[idx] = row with { SeriesOrder = newOrder };
        }
    }

    public record SeriesBookRow(int Id, string Title, string Author, int? SeriesOrder);
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
