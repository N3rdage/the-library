using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Application.Wishlist;
using BookTracker.Application.Works;
using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Web.ViewModels;

public class AIAssistantViewModel(
    IDispatcher dispatcher,
    AIProviderFactory providerFactory)
{
    private IAIAssistantService AiService => providerFactory.GetService();

    public AIProvider ActiveProvider => providerFactory.ActiveProvider;
    public IReadOnlyList<AIProvider> AvailableProviders => providerFactory.AvailableProviders;

    public void SwitchProvider(AIProvider provider) => providerFactory.SwitchProvider(provider);

    // Genre cleanup
    public List<BookGenreRow> BooksNeedingGenres { get; private set; } = [];
    public bool GenresLoaded { get; private set; }
    public int? SuggestingForBookId { get; private set; }
    public int? SuggestedBookId { get; private set; }
    public GenreSuggestionResult? CurrentSuggestion { get; private set; }
    public string? GenreError { get; private set; }

    public int CallCount => AiService.CallCount;

    public async Task LoadBooksNeedingGenresAsync()
    {
        BooksNeedingGenres = await dispatcher.Query(new GetBooksNeedingGenres());
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
            CurrentSuggestion = await AiService.SuggestGenresAsync(
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
        var updatedGenres = await dispatcher.Send(new AddGenresToWork(bookId, genreNames));
        if (updatedGenres is null) return;

        // Update the local list
        var row = BooksNeedingGenres.FirstOrDefault(b => b.Id == bookId);
        if (row is not null)
        {
            var idx = BooksNeedingGenres.IndexOf(row);
            BooksNeedingGenres[idx] = row with { CurrentGenres = [.. updatedGenres] };
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
            CollectionSuggestion = await AiService.SuggestCollectionsAsync();
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
        var seriesType = grouping.Type.Equals("Series", StringComparison.OrdinalIgnoreCase)
            ? SeriesType.Series
            : SeriesType.Collection;

        await dispatcher.Send(new CreateSeriesFromSuggestion(
            grouping.SuggestedName, seriesType, grouping.Reasoning, grouping.BookTitles));
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
            ShoppingSuggestion = await AiService.SuggestShoppingListAsync();
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
        // Already on the wishlist (same Title + Author) → nothing to do.
        if (await dispatcher.Query(new GetWishlistItemExists(rec.Title, rec.Author)))
            return;

        await dispatcher.Send(new AddWishlistItem(
            rec.Title, rec.Author, WishlistPriority.Medium, Isbns: [], CoverUrl: null));
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
            AdvisorResult = await AiService.AssessBookAsync(BookAdvisorQuery.Trim());
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
}
