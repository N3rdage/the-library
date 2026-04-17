using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class AIAssistantViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IAIAssistantService aiService)
{
    // Genre cleanup
    public List<BookGenreRow> BooksNeedingGenres { get; private set; } = [];
    public bool GenresLoaded { get; private set; }
    public int? SuggestingForBookId { get; private set; }
    public int? SuggestedBookId { get; private set; }
    public GenreSuggestionResult? CurrentSuggestion { get; private set; }
    public string? GenreError { get; private set; }

    public int CallCount => aiService.CallCount;

    public async Task LoadBooksNeedingGenresAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        BooksNeedingGenres = await db.Books
            .Include(b => b.Genres)
            .OrderBy(b => b.Genres.Count)
            .ThenBy(b => b.Title)
            .Take(50)
            .Select(b => new BookGenreRow(
                b.Id,
                b.Title,
                b.Subtitle,
                b.Author,
                b.Genres.Select(g => g.Name).ToList()))
            .ToListAsync();

        GenresLoaded = true;
    }

    public async Task SuggestGenresForBookAsync(int bookId)
    {
        var book = BooksNeedingGenres.FirstOrDefault(b => b.Id == bookId);
        if (book is null) return;

        SuggestingForBookId = bookId;
        SuggestedBookId = bookId;
        CurrentSuggestion = null;
        GenreError = null;

        try
        {
            CurrentSuggestion = await aiService.SuggestGenresAsync(
                book.Title, book.Author, book.Subtitle, book.CurrentGenres);
        }
        catch (Exception ex)
        {
            GenreError = $"AI request failed: {ex.Message}";
        }
        finally
        {
            SuggestingForBookId = null;
        }
    }

    public async Task AcceptGenreSuggestionsAsync(int bookId, IReadOnlyList<string> genreNames)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var book = await db.Books.Include(b => b.Genres).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book is null) return;

        var genresToAdd = await db.Genres
            .Where(g => genreNames.Contains(g.Name))
            .ToListAsync();

        foreach (var genre in genresToAdd)
        {
            if (!book.Genres.Any(g => g.Id == genre.Id))
                book.Genres.Add(genre);
        }

        await db.SaveChangesAsync();

        // Update the local list
        var row = BooksNeedingGenres.FirstOrDefault(b => b.Id == bookId);
        if (row is not null)
        {
            var idx = BooksNeedingGenres.IndexOf(row);
            var updatedGenres = book.Genres.Select(g => g.Name).ToList();
            BooksNeedingGenres[idx] = row with { CurrentGenres = updatedGenres };
        }

        CurrentSuggestion = null;
    }

    public void DismissSuggestion()
    {
        CurrentSuggestion = null;
        SuggestedBookId = null;
        GenreError = null;
    }

    // Collection cataloguing
    public CollectionSuggestionResult? CollectionSuggestion { get; private set; }
    public bool SuggestingCollections { get; private set; }
    public string? CollectionError { get; private set; }

    public async Task SuggestCollectionsAsync()
    {
        SuggestingCollections = true;
        CollectionSuggestion = null;
        CollectionError = null;

        try
        {
            CollectionSuggestion = await aiService.SuggestCollectionsAsync();
        }
        catch (Exception ex)
        {
            CollectionError = $"AI request failed: {ex.Message}";
        }
        finally
        {
            SuggestingCollections = false;
        }
    }

    public async Task CreateCollectionFromSuggestionAsync(CollectionGrouping grouping)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var seriesType = grouping.Type.Equals("Series", StringComparison.OrdinalIgnoreCase)
            ? SeriesType.Series
            : SeriesType.Collection;

        // Check if series already exists
        var existing = await db.Series.FirstOrDefaultAsync(s => s.Name == grouping.SuggestedName);
        if (existing is not null) return;

        var series = new Series
        {
            Name = grouping.SuggestedName,
            Type = seriesType,
            Description = grouping.Reasoning
        };

        // Try to set the author if all books share the same one
        var matchedBooks = await db.Books
            .Where(b => grouping.BookTitles.Contains(b.Title))
            .ToListAsync();

        var authors = matchedBooks.Select(b => b.Author).Distinct().ToList();
        if (authors.Count == 1)
            series.Author = authors[0];

        db.Series.Add(series);
        await db.SaveChangesAsync();

        // Assign books to the series
        var order = 1;
        foreach (var book in matchedBooks)
        {
            if (book.SeriesId is null)
            {
                book.SeriesId = series.Id;
                book.SeriesOrder = order++;
            }
        }
        await db.SaveChangesAsync();
    }

    public void DismissCollections()
    {
        CollectionSuggestion = null;
        CollectionError = null;
    }

    // Shopping suggestions
    public ShoppingSuggestionResult? ShoppingSuggestion { get; private set; }
    public bool SuggestingShopping { get; private set; }
    public string? ShoppingError { get; private set; }

    public async Task SuggestShoppingListAsync()
    {
        SuggestingShopping = true;
        ShoppingSuggestion = null;
        ShoppingError = null;

        try
        {
            ShoppingSuggestion = await aiService.SuggestShoppingListAsync();
        }
        catch (Exception ex)
        {
            ShoppingError = $"AI request failed: {ex.Message}";
        }
        finally
        {
            SuggestingShopping = false;
        }
    }

    public async Task AddRecommendationToWishlistAsync(BookRecommendation rec)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Check if already on wishlist
        if (await db.WishlistItems.AnyAsync(w => w.Title == rec.Title && w.Author == rec.Author))
            return;

        db.WishlistItems.Add(new WishlistItem
        {
            Title = rec.Title,
            Author = rec.Author,
            Priority = WishlistPriority.Medium
        });
        await db.SaveChangesAsync();
    }

    public void DismissShoppingSuggestions()
    {
        ShoppingSuggestion = null;
        ShoppingError = null;
    }

    // Book advisor
    public string BookAdvisorQuery { get; set; } = "";
    public BookAdvisorResult? AdvisorResult { get; private set; }
    public bool Advising { get; private set; }
    public string? AdvisorError { get; private set; }

    public async Task AssessBookAsync()
    {
        if (string.IsNullOrWhiteSpace(BookAdvisorQuery)) return;

        Advising = true;
        AdvisorResult = null;
        AdvisorError = null;

        try
        {
            AdvisorResult = await aiService.AssessBookAsync(BookAdvisorQuery.Trim());
        }
        catch (Exception ex)
        {
            AdvisorError = $"AI request failed: {ex.Message}";
        }
        finally
        {
            Advising = false;
        }
    }

    public void ClearAdvisor()
    {
        AdvisorResult = null;
        AdvisorError = null;
        BookAdvisorQuery = "";
    }

    public record BookGenreRow(
        int Id, string Title, string? Subtitle, string Author,
        List<string> CurrentGenres);
}
