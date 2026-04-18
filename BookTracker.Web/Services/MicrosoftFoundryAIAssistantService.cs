using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

/// <summary>
/// AI assistant implementation using Claude models hosted on Microsoft Foundry.
/// Uses the Anthropic SDK with a custom HttpClient pointing at the Azure endpoint.
/// </summary>
public class MicrosoftFoundryAIAssistantService(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    MicrosoftFoundryOptions options) : IAIAssistantService
{
    private AnthropicClient? _client;

    public int CallCount { get; private set; }

    private AnthropicClient GetClient()
    {
        if (_client is null)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/")
            };
            _client = new AnthropicClient(options.ApiKey, httpClient);
        }
        return _client;
    }

    public async Task<string?> ExtractIsbnFromImageAsync(string base64Jpeg, CancellationToken ct = default)
    {
        var messages = new List<Message>
        {
            new()
            {
                Role = RoleType.User,
                Content = new List<ContentBase>
                {
                    new ImageContent
                    {
                        Source = new ImageSource { MediaType = "image/jpeg", Data = base64Jpeg }
                    },
                    new TextContent
                    {
                        Text = "Extract the ISBN number from this image. Return ONLY the ISBN (10 or 13 characters). ISBN-10 may end with the letter X as a check digit — include it if present. Return nothing else. If you cannot find an ISBN, return the word NONE."
                    }
                }
            }
        };

        var response = await CallAsync(messages, options.FastDeployment, 50, ct);
        var responseText = (response.Message?.ToString() ?? "").Trim();

        if (responseText.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return null;

        var cleaned = new string(responseText.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray());
        return cleaned.Length is 10 or 13 ? cleaned : null;
    }

    public async Task<GenreSuggestionResult> SuggestGenresAsync(
        string title, string author, string? subtitle,
        IReadOnlyList<string> currentGenres, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var allGenres = await db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new { g.Name, ParentName = g.ParentGenre != null ? g.ParentGenre.Name : null })
            .ToListAsync(ct);

        var taxonomyText = string.Join("\n", allGenres.Select(g =>
            g.ParentName is not null ? $"  - {g.Name} (sub-genre of {g.ParentName})" : $"- {g.Name}"));

        var currentGenresText = currentGenres.Count > 0
            ? $"Currently assigned genres: {string.Join(", ", currentGenres)}"
            : "No genres currently assigned.";

        var bookDescription = subtitle is not null
            ? $"\"{title}: {subtitle}\" by {author}"
            : $"\"{title}\" by {author}";

        var systemPrompt = $@"You are a librarian helping categorise books into a preset genre taxonomy. You must ONLY suggest genres from the following list — do not invent new genres.

Genre taxonomy:
{taxonomyText}

Rules:
- Suggest 1-5 genres that best fit the book.
- Include parent genres when suggesting sub-genres.
- Only use exact genre names from the list above.
- Respond with valid JSON: {{""genres"": [""Genre 1""], ""reasoning"": ""Why.""}}";

        var userMessage = $"Suggest genres for this book:\n{bookDescription}\n\n{currentGenresText}\n\nRespond with JSON only.";

        var response = await CallWithSystemAsync(systemPrompt, userMessage, options.FastDeployment, options.MaxTokens, ct);
        return SharedParsers.ParseGenreSuggestion(response.Message?.ToString() ?? "", allGenres.Select(g => g.Name).ToHashSet());
    }

    public async Task<CollectionSuggestionResult> SuggestCollectionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var uncategorised = await db.Books.Where(b => b.SeriesId == null)
            .OrderBy(b => b.Author).ThenBy(b => b.Title)
            .Select(b => new { b.Title, b.Author }).ToListAsync(ct);

        if (uncategorised.Count == 0)
            return new CollectionSuggestionResult([], "All books are already in a series or collection.");

        var existingSeries = await db.Series.OrderBy(s => s.Name)
            .Select(s => new { s.Name, s.Type, BookCount = s.Books.Count }).ToListAsync(ct);

        var booksText = string.Join("\n", uncategorised.Select(b => $"- \"{b.Title}\" by {b.Author}"));
        var seriesText = existingSeries.Count > 0
            ? "Existing series/collections:\n" + string.Join("\n", existingSeries.Select(s => $"- {s.Name} ({s.Type}, {s.BookCount} books)"))
            : "No existing series or collections.";

        var response = await CallWithSystemAsync(SharedParsers.BuildCollectionSystemPrompt(seriesText),
            $"Here are the books not in any series:\n\n{booksText}\n\nSuggest groupings. Respond with JSON only.",
            options.FastDeployment, 2048, ct);
        return SharedParsers.ParseCollectionSuggestion(response.Message?.ToString() ?? "");
    }

    public async Task<ShoppingSuggestionResult> SuggestShoppingListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var context = await SharedParsers.BuildLibraryContextAsync(db, ct);

        var response = await CallWithSystemAsync(SharedParsers.ShoppingSystemPrompt,
            $"{context}\n\nSuggest 5-10 books to look for. Respond with JSON only.",
            options.FastDeployment, 2048, ct);
        return SharedParsers.ParseShoppingSuggestion(response.Message?.ToString() ?? "");
    }

    public async Task<BookAdvisorResult> AssessBookAsync(string query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var allBooks = await db.Books.Include(b => b.Genres)
            .OrderBy(b => b.Author).ThenBy(b => b.Title)
            .Select(b => new { b.Title, b.Author, b.Rating, Genres = b.Genres.Select(g => g.Name).ToList() })
            .ToListAsync(ct);

        var booksText = allBooks.Count > 0
            ? string.Join("\n", allBooks.Select(b => $"- \"{b.Title}\" by {b.Author} | {b.Rating}/5 | {string.Join(", ", b.Genres)}"))
            : "Library is empty.";

        var response = await CallWithSystemAsync(SharedParsers.BuildAdvisorSystemPrompt(booksText),
            $"I'm considering: {query}\n\nShould I get it? Respond with JSON only.",
            options.DeepDeployment, 2048, ct);
        return SharedParsers.ParseBookAdvisor(response.Message?.ToString() ?? "");
    }

    private async Task<MessageResponse> CallAsync(List<Message> messages, string model, int maxTokens, CancellationToken ct)
    {
        var parameters = new MessageParameters { Messages = messages, MaxTokens = maxTokens, Model = model, Stream = false };
        var response = await GetClient().Messages.GetClaudeMessageAsync(parameters, ct);
        CallCount++;
        return response;
    }

    private async Task<MessageResponse> CallWithSystemAsync(string systemPrompt, string userMessage, string model, int maxTokens, CancellationToken ct)
    {
        var parameters = new MessageParameters
        {
            Messages = [new(RoleType.User, userMessage)],
            MaxTokens = maxTokens,
            Model = model,
            Stream = false,
            System = [new SystemMessage(systemPrompt, new CacheControl { Type = CacheControlType.ephemeral })],
            PromptCaching = PromptCacheType.FineGrained
        };
        var response = await GetClient().Messages.GetClaudeMessageAsync(parameters, ct);
        CallCount++;
        return response;
    }
}
