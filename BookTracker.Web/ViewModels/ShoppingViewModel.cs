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

    // Series gaps
    public List<SeriesGap> SeriesGaps { get; private set; } = [];
    public bool GapsLoaded { get; private set; }

    public async Task LoadSeriesGapsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Structured series with a known expected count where we're missing books
        var incompleteSeries = await db.Series
            .Include(s => s.Books)
            .Where(s => s.Type == SeriesType.Series && s.ExpectedCount != null)
            .ToListAsync();

        SeriesGaps = incompleteSeries
            .Where(s => s.Books.Count < s.ExpectedCount!.Value)
            .OrderBy(s => s.Name)
            .Select(s =>
            {
                var ownedPositions = s.Books
                    .Where(b => b.SeriesOrder.HasValue)
                    .Select(b => b.SeriesOrder!.Value)
                    .OrderBy(n => n)
                    .ToList();

                var missing = new List<int>();
                for (int i = 1; i <= s.ExpectedCount!.Value; i++)
                {
                    if (!ownedPositions.Contains(i))
                        missing.Add(i);
                }

                return new SeriesGap(
                    s.Id,
                    s.Name,
                    s.Author,
                    s.Books.Count,
                    s.ExpectedCount.Value,
                    missing,
                    s.Books.OrderBy(b => b.SeriesOrder ?? int.MaxValue)
                        .Select(b => new OwnedSeriesBook(b.Id, b.Title, b.SeriesOrder))
                        .ToList());
            })
            .ToList();

        GapsLoaded = true;
    }

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

            var editions = await db.Editions
                .Include(e => e.Book)
                    .ThenInclude(b => b.Series)
                .Include(e => e.Copies)
                .Where(e => e.Isbn == isbn)
                .ToListAsync();

            if (editions.Count > 0)
            {
                var book = editions[0].Book;
                var copyCount = editions.SelectMany(e => e.Copies).Count();
                var seriesInfo = await GetSeriesInfoAsync(db, book);
                Result = new LookupResult(
                    Found: true,
                    BookId: book.Id,
                    Title: book.Title,
                    Author: book.Author,
                    CopyCount: copyCount,
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
                .Include(b => b.Editions)
                    .ThenInclude(e => e.Copies)
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
                    CopyCount: book.Editions.SelectMany(e => e.Copies).Count(),
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
                        b.Id, b.Title, b.Author, b.Editions.SelectMany(e => e.Copies).Count(), b.DefaultCoverArtUrl
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
            .Include(b => b.Editions)
                .ThenInclude(e => e.Copies)
            .Include(b => b.Series)
            .FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null) return;

        var seriesInfo = await GetSeriesInfoAsync(db, book);
        Result = new LookupResult(
            Found: true,
            BookId: book.Id,
            Title: book.Title,
            Author: book.Author,
            CopyCount: book.Editions.SelectMany(e => e.Copies).Count(),
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

    // Shopping list (wishlist)
    public List<ShoppingListItem> ShoppingList { get; private set; } = [];
    public bool ShoppingListLoaded { get; private set; }
    public bool ShowingQuickAdd { get; set; }
    public QuickAddInput QuickAdd { get; set; } = new();

    public async Task LoadShoppingListAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        ShoppingList = await db.WishlistItems
            .Include(w => w.Series)
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.Title)
            .Select(w => new ShoppingListItem(
                w.Id, w.Title, w.Author, w.Priority, w.Isbn,
                w.Series != null ? w.Series.Name : null,
                w.SeriesOrder))
            .ToListAsync();
        ShoppingListLoaded = true;
    }

    public async Task AddToShoppingListAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickAdd.Title)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var item = new WishlistItem
        {
            Title = QuickAdd.Title.Trim(),
            Author = string.IsNullOrWhiteSpace(QuickAdd.Author) ? "Unknown" : QuickAdd.Author.Trim(),
            Priority = QuickAdd.Priority,
            Isbn = string.IsNullOrWhiteSpace(QuickAdd.Isbn) ? null : QuickAdd.Isbn.Trim()
        };

        db.WishlistItems.Add(item);
        await db.SaveChangesAsync();

        ShoppingList.Insert(0, new ShoppingListItem(
            item.Id, item.Title, item.Author, item.Priority, item.Isbn, null, null));
        // Re-sort by priority
        ShoppingList = ShoppingList
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Title)
            .ToList();

        QuickAdd = new();
        ShowingQuickAdd = false;
    }

    public async Task RemoveFromShoppingListAsync(int itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.WishlistItems.FindAsync(itemId);
        if (item is not null)
        {
            db.WishlistItems.Remove(item);
            await db.SaveChangesAsync();
        }
        ShoppingList.RemoveAll(i => i.Id == itemId);
    }

    /// <summary>
    /// Marks a wishlist item as "bought" — creates a Book + Edition + Copy
    /// with follow-up tag and default values, then removes the wishlist item.
    /// Returns the new book ID for navigation.
    /// </summary>
    public async Task<int?> MarkAsBoughtAsync(ShoppingListItem item)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var followUpTag = await db.Tags.FirstOrDefaultAsync(t => t.Name == "follow-up");
        if (followUpTag is null)
        {
            followUpTag = new Tag { Name = "follow-up" };
            db.Tags.Add(followUpTag);
        }

        var book = new Book
        {
            Title = item.Title,
            Author = item.Author,
            Tags = [followUpTag],
            Editions = []
        };

        if (!string.IsNullOrWhiteSpace(item.Isbn))
        {
            book.Editions.Add(new Edition
            {
                Isbn = item.Isbn,
                Format = BookFormat.Softcopy,
                Copies = [new Copy { Condition = BookCondition.Good }]
            });
        }

        db.Books.Add(book);

        var wishlistItem = await db.WishlistItems.FindAsync(item.Id);
        if (wishlistItem is not null)
            db.WishlistItems.Remove(wishlistItem);

        await db.SaveChangesAsync();

        ShoppingList.RemoveAll(i => i.Id == item.Id);
        return book.Id;
    }

    public static string PriorityBadgeClass(WishlistPriority p) => p switch
    {
        WishlistPriority.High => "bg-danger",
        WishlistPriority.Medium => "bg-warning text-dark",
        WishlistPriority.Low => "bg-secondary",
        _ => "bg-light text-dark"
    };

    public class QuickAddInput
    {
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Isbn { get; set; }
        public WishlistPriority Priority { get; set; } = WishlistPriority.Medium;
    }

    public record ShoppingListItem(
        int Id, string Title, string Author, WishlistPriority Priority,
        string? Isbn, string? SeriesName, int? SeriesOrder);

    public record SeriesGap(
        int SeriesId, string SeriesName, string? Author,
        int OwnedCount, int ExpectedCount,
        List<int> MissingPositions,
        List<OwnedSeriesBook> OwnedBooks);

    public record OwnedSeriesBook(int Id, string Title, int? SeriesOrder);
}
