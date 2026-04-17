using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class ShoppingViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    // "Do I have this?" state
    public string SearchTerm { get; set; } = "";
    public string? ScannedIsbn { get; set; }
    public LookupResult? Result { get; private set; }
    public bool Searching { get; private set; }

    public void ClearResult()
    {
        Result = null;
        ScannedIsbn = null;
    }

    public async Task SearchByIsbnAsync(string isbn)
    {
        Searching = true;
        try
        {
            ScannedIsbn = isbn;
            SearchTerm = "";
            await using var db = await dbFactory.CreateDbContextAsync();

            var copies = await db.BookCopies
                .Include(c => c.Book)
                    .ThenInclude(b => b.Series)
                .Where(c => c.Isbn == isbn)
                .ToListAsync();

            if (copies.Count > 0)
            {
                var book = copies[0].Book;
                var seriesInfo = await GetSeriesInfoAsync(db, book);
                Result = new LookupResult(
                    Found: true,
                    BookId: book.Id,
                    Title: book.Title,
                    Author: book.Author,
                    CopyCount: copies.Count,
                    CoverUrl: book.DefaultCoverArtUrl,
                    SeriesInfo: seriesInfo);
            }
            else
            {
                Result = new LookupResult(
                    Found: false,
                    BookId: null,
                    Title: null,
                    Author: null,
                    CopyCount: 0,
                    CoverUrl: null,
                    SeriesInfo: null);
            }
        }
        finally
        {
            Searching = false;
        }
    }

    public async Task SearchByTextAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm)) return;

        Searching = true;
        try
        {
            ScannedIsbn = null;
            var term = SearchTerm.Trim();
            await using var db = await dbFactory.CreateDbContextAsync();

            var books = await db.Books
                .Include(b => b.Copies)
                .Include(b => b.Series)
                .Where(b => b.Title.Contains(term) || b.Author.Contains(term))
                .OrderBy(b => b.Title)
                .Take(10)
                .ToListAsync();

            if (books.Count == 1)
            {
                var book = books[0];
                var seriesInfo = await GetSeriesInfoAsync(db, book);
                Result = new LookupResult(
                    Found: true,
                    BookId: book.Id,
                    Title: book.Title,
                    Author: book.Author,
                    CopyCount: book.Copies.Count,
                    CoverUrl: book.DefaultCoverArtUrl,
                    SeriesInfo: seriesInfo);
            }
            else if (books.Count > 1)
            {
                Result = new LookupResult(
                    Found: true,
                    BookId: null,
                    Title: null,
                    Author: null,
                    CopyCount: 0,
                    CoverUrl: null,
                    SeriesInfo: null,
                    MultipleResults: books.Select(b => new SearchResultItem(
                        b.Id, b.Title, b.Author, b.Copies.Count, b.DefaultCoverArtUrl
                    )).ToList());
            }
            else
            {
                Result = new LookupResult(
                    Found: false,
                    BookId: null,
                    Title: null,
                    Author: null,
                    CopyCount: 0,
                    CoverUrl: null,
                    SeriesInfo: null);
            }
        }
        finally
        {
            Searching = false;
        }
    }

    public async Task SelectBookAsync(int bookId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books
            .Include(b => b.Copies)
            .Include(b => b.Series)
            .FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null) return;

        var seriesInfo = await GetSeriesInfoAsync(db, book);
        Result = new LookupResult(
            Found: true,
            BookId: book.Id,
            Title: book.Title,
            Author: book.Author,
            CopyCount: book.Copies.Count,
            CoverUrl: book.DefaultCoverArtUrl,
            SeriesInfo: seriesInfo);
    }

    private static async Task<SeriesInfo?> GetSeriesInfoAsync(BookTrackerDbContext db, Book book)
    {
        if (book.SeriesId is null || book.Series is null)
            return null;

        var series = book.Series;
        var booksInSeries = await db.Books
            .Where(b => b.SeriesId == series.Id)
            .CountAsync();

        return new SeriesInfo(
            series.Id,
            series.Name,
            series.Type,
            booksInSeries,
            series.ExpectedCount);
    }

    public record LookupResult(
        bool Found,
        int? BookId,
        string? Title,
        string? Author,
        int CopyCount,
        string? CoverUrl,
        SeriesInfo? SeriesInfo,
        List<SearchResultItem>? MultipleResults = null);

    public record SearchResultItem(int Id, string Title, string Author, int CopyCount, string? CoverUrl);

    public record SeriesInfo(int SeriesId, string SeriesName, SeriesType Type, int OwnedCount, int? ExpectedCount);
}
