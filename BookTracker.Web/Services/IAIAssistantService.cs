namespace BookTracker.Web.Services;

public interface IAIAssistantService
{
    /// <summary>
    /// Suggests genres for a book from the preset taxonomy.
    /// Returns genre names that match entries in the Genres table.
    /// </summary>
    Task<GenreSuggestionResult> SuggestGenresAsync(string title, string author, string? subtitle, IReadOnlyList<string> currentGenres, CancellationToken ct = default);

    /// <summary>
    /// Analyses uncategorised books and suggests series/collection groupings.
    /// </summary>
    Task<CollectionSuggestionResult> SuggestCollectionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Suggests books to look for based on incomplete series, reading preferences, and library patterns.
    /// </summary>
    Task<ShoppingSuggestionResult> SuggestShoppingListAsync(CancellationToken ct = default);

    /// <summary>Number of API calls made in this service instance's lifetime.</summary>
    int CallCount { get; }
}

public record GenreSuggestionResult(
    IReadOnlyList<string> SuggestedGenres,
    string Reasoning);

public record CollectionSuggestionResult(
    IReadOnlyList<CollectionGrouping> Groupings,
    string Summary);

public record CollectionGrouping(
    string SuggestedName,
    string Type,
    string Reasoning,
    IReadOnlyList<string> BookTitles);

public record ShoppingSuggestionResult(
    IReadOnlyList<BookRecommendation> Recommendations,
    string Summary);

public record BookRecommendation(
    string Title,
    string Author,
    string Reasoning,
    string? SeriesContext);
