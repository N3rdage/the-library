using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
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

        // Structured series with a known expected count where we're missing works.
        // Series → Works → Books gives us the parent book(s) to link to.
        var incompleteSeries = await db.Series
            .Include(s => s.Works).ThenInclude(w => w.Books)
            .Where(s => s.Type == SeriesType.Series && s.ExpectedCount != null)
            .ToListAsync();

        SeriesGaps = incompleteSeries
            .Where(s => s.Works.Count < s.ExpectedCount!.Value)
            .OrderBy(s => s.Name)
            .Select(s =>
            {
                var ownedPositions = s.Works
                    .Where(w => w.SeriesOrder.HasValue)
                    .Select(w => w.SeriesOrder!.Value)
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
                    s.Works.Count,
                    s.ExpectedCount.Value,
                    missing,
                    s.Works.OrderBy(w => w.SeriesOrder ?? int.MaxValue)
                        // Display the work title; link to the first containing
                        // book (if any) so the user can navigate to it.
                        .Select(w => new OwnedSeriesBook(
                            w.Books.FirstOrDefault()?.Id ?? 0,
                            w.Title,
                            w.SeriesOrder))
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
                    .ThenInclude(b => b.Works).ThenInclude(w => w.Series)
                .Include(e => e.Book)
                    .ThenInclude(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
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
                    Author: PrimaryAuthor(book),
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
                .Include(b => b.Works).ThenInclude(w => w.Series)
                .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
                .Where(b => b.Title.Contains(term) || b.Works.Any(w => w.Title.Contains(term) || w.Authors.Any(a => a.Name.Contains(term))))
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
                    Author: PrimaryAuthor(book),
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
                        b.Id, b.Title, PrimaryAuthor(b), b.Editions.SelectMany(e => e.Copies).Count(), b.DefaultCoverArtUrl
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
            .Include(b => b.Works).ThenInclude(w => w.Series)
            .Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null) return;

        var seriesInfo = await GetSeriesInfoAsync(db, book);
        Result = new LookupResult(
            Found: true,
            BookId: book.Id,
            Title: book.Title,
            Author: PrimaryAuthor(book),
            CopyCount: book.Editions.SelectMany(e => e.Copies).Count(),
            CoverUrl: book.DefaultCoverArtUrl,
            SeriesInfo: seriesInfo);
    }

    // For display in shopping search results — most books have a single Work
    // and therefore a single author, but compendiums may span several. Comma-
    // join to keep things readable. Null-tolerant on Author because every
    // caller is expected to Include(w => w.Author) but forgetting has
    // shipped a user-visible NullReferenceException in the past — here we
    // degrade to "(unknown)" rather than crashing the lookup.
    private static string PrimaryAuthor(Book book)
    {
        var names = book.Works
            .SelectMany(w => w.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author?.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
        return names.Count > 0 ? string.Join(", ", names!) : "(unknown)";
    }

    private static async Task<SeriesInfo?> GetSeriesInfoAsync(BookTrackerDbContext db, Book book)
    {
        // Series membership is per-Work; the primary work's series (if any)
        // stands in for the whole book in the shopping UI.
        var primary = book.Works.FirstOrDefault(w => w.SeriesId.HasValue);
        if (primary is null || primary.Series is null) return null;

        var series = primary.Series;
        var worksInSeries = await db.Works
            .Where(w => w.SeriesId == series.Id)
            .CountAsync();

        return new SeriesInfo(
            series.Id,
            series.Name,
            series.Type,
            worksInSeries,
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

        var author = await AuthorResolver.FindOrCreateAsync(item.Author, db);
        var work = new Work { Title = item.Title };
        AuthorResolver.AssignAuthors(work, [author]);
        var book = new Book
        {
            Title = item.Title,
            Tags = [followUpTag],
            Editions = [],
            Works = [work]
        };

        if (!string.IsNullOrWhiteSpace(item.Isbn))
        {
            book.Editions.Add(new Edition
            {
                Isbn = item.Isbn,
                Format = BookFormat.TradePaperback,
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
