using System.Globalization;
using BookTracker.Application;
using BookTracker.Application.Ai;
using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Application.Wishlist;
using BookTracker.Application.Works;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.Extensions.Options;

namespace BookTracker.Web.ViewModels;

public class AIAssistantViewModel(
    IDispatcher dispatcher,
    AIProviderFactory providerFactory,
    IOptions<AIOptions> aiOptions) : DispatchingViewModel
{
    private IAIAssistantService AiService => providerFactory.GetService();

    public AIProvider ActiveProvider => providerFactory.ActiveProvider;
    public IReadOnlyList<AIProvider> AvailableProviders => providerFactory.AvailableProviders;

    public void SwitchProvider(AIProvider provider) => providerFactory.SwitchProvider(provider);

    // Usage summary: the recorded tokens across every provider, priced from
    // AI:Prices. Loaded when the page initialises and refreshed after each AI
    // action so the line reflects the call just made.
    public AiUsageSummary? UsageSummary { get; private set; }

    /// <summary>The one-line reading of this month's usage, totalled across
    /// providers, e.g. "This month: 12 calls, 84,210 in / 6,120 out tokens,
    /// about $0.35 (2 models without a price in AI:Prices)". Null until the
    /// summary has loaded.</summary>
    public string? UsageLine => UsageSummary is null ? null : FormatUsageLine(UsageSummary.ThisMonth);

    public async Task LoadUsageSummaryAsync()
    {
        UsageSummary = await dispatcher.Query(new GetAiUsageSummary(aiOptions.Value.Prices));
    }

    private static string FormatUsageLine(IReadOnlyList<AiUsageLine> lines)
    {
        var inv = CultureInfo.InvariantCulture;
        var calls = lines.Sum(l => l.Calls);
        var text = string.Format(inv, "This month: {0:N0} {1}, {2:N0} in / {3:N0} out tokens",
            calls, calls == 1 ? "call" : "calls", lines.Sum(l => l.InputTokens), lines.Sum(l => l.OutputTokens));

        var priced = lines.Where(l => l.CostUsd is not null).ToList();
        if (priced.Count > 0)
            text += string.Format(inv, ", about ${0:0.00}", priced.Sum(l => l.CostUsd!.Value));

        var unpriced = lines.Count - priced.Count;
        if (unpriced > 0)
            text += string.Format(inv, " ({0} {1} without a price in AI:Prices)", unpriced, unpriced == 1 ? "model" : "models");

        return text;
    }

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
        await LoadUsageSummaryAsync();
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
        Error = null;

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
        await LoadUsageSummaryAsync();
    }

    public async Task CreateCollectionFromSuggestionAsync(CollectionGrouping grouping)
    {
        var seriesType = grouping.Type.Equals("Series", StringComparison.OrdinalIgnoreCase)
            ? SeriesType.Series
            : SeriesType.Collection;

        // A blank or clashing name lands in Error, shown above the groupings; the
        // suggestions stay on screen.
        await DispatchAsync(() => dispatcher.Send(new CreateSeriesFromSuggestion(
            grouping.SuggestedName, seriesType, grouping.Reasoning, grouping.BookTitles)));
    }

    public void DismissCollections()
    {
        CollectionSuggestion = null;
        CollectionError = null;
        Error = null;
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
        await LoadUsageSummaryAsync();
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
        await LoadUsageSummaryAsync();
    }

    public void ClearAdvisor()
    {
        AdvisorResult = null;
        AdvisorError = null;
        BookAdvisorQuery = "";
    }
}
