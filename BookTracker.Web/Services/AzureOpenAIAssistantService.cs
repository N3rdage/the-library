using System.ClientModel;
using Azure.AI.OpenAI;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;

namespace BookTracker.Web.Services;

/// <summary>
/// AI assistant implementation using Azure OpenAI (GPT-4o).
/// </summary>
public class AzureOpenAIAssistantService(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    AzureOpenAIOptions options) : IAIAssistantService
{
    private ChatClient? _client;

    public int CallCount { get; private set; }

    private ChatClient GetClient()
    {
        if (_client is null)
        {
            var azureClient = new AzureOpenAIClient(
                new Uri(options.Endpoint),
                new ApiKeyCredential(options.ApiKey));
            _client = azureClient.GetChatClient(options.Deployment);
        }
        return _client;
    }

    public async Task<string?> ExtractIsbnFromImageAsync(string base64Jpeg, CancellationToken ct = default)
    {
        var imageBytes = Convert.FromBase64String(base64Jpeg);
        var imageContent = ChatMessageContentPart.CreateImagePart(
            BinaryData.FromBytes(imageBytes), "image/jpeg");
        var textContent = ChatMessageContentPart.CreateTextPart(
            "Extract the ISBN number from this image. Return ONLY the ISBN (10 or 13 characters). ISBN-10 may end with the letter X as a check digit — include it if present. Return nothing else. If you cannot find an ISBN, return the word NONE.");

        var messages = new List<ChatMessage>
        {
            new UserChatMessage(imageContent, textContent)
        };

        var response = await CallAsync(messages, 50, ct);
        var responseText = response.Trim();

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

        var responseText = await CallWithSystemAsync(systemPrompt, userMessage, options.MaxTokens, ct);
        return SharedParsers.ParseGenreSuggestion(responseText, allGenres.Select(g => g.Name).ToHashSet());
    }

    public async Task<CollectionSuggestionResult> SuggestCollectionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Books whose primary work isn't in any series. Project the work's
        // author since Book no longer carries Author directly.
        var uncategorised = await db.Books
            .Include(b => b.Works)
            .Where(b => b.Works.All(w => w.SeriesId == null))
            .OrderBy(b => b.Title)
            .Select(b => new
            {
                b.Title,
                Author = string.Join(", ", b.Works.SelectMany(w => w.Authors.Select(a => a.Name)).Distinct())
            })
            .ToListAsync(ct);

        if (uncategorised.Count == 0)
            return new CollectionSuggestionResult([], "All books are already in a series or collection.");

        var existingSeries = await db.Series.OrderBy(s => s.Name)
            .Select(s => new { s.Name, s.Type, WorkCount = s.Works.Count }).ToListAsync(ct);

        var booksText = string.Join("\n", uncategorised.Select(b => $"- \"{b.Title}\" by {b.Author}"));
        var seriesText = existingSeries.Count > 0
            ? "Existing series/collections:\n" + string.Join("\n", existingSeries.Select(s => $"- {s.Name} ({s.Type}, {s.WorkCount} works)"))
            : "No existing series or collections.";

        var responseText = await CallWithSystemAsync(SharedParsers.BuildCollectionSystemPrompt(seriesText),
            $"Here are the books not in any series:\n\n{booksText}\n\nSuggest groupings. Respond with JSON only.", 2048, ct);
        return SharedParsers.ParseCollectionSuggestion(responseText);
    }

    public async Task<ShoppingSuggestionResult> SuggestShoppingListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var context = await SharedParsers.BuildLibraryContextAsync(db, ct);

        var responseText = await CallWithSystemAsync(SharedParsers.ShoppingSystemPrompt,
            $"{context}\n\nSuggest 5-10 books to look for. Respond with JSON only.", 2048, ct);
        return SharedParsers.ParseShoppingSuggestion(responseText);
    }

    public async Task<BookAdvisorResult> AssessBookAsync(string query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var allBooks = await db.Books
            .Include(b => b.Works).ThenInclude(w => w.Genres)
            .OrderBy(b => b.Title)
            .Select(b => new
            {
                b.Title,
                Author = string.Join(", ", b.Works.SelectMany(w => w.Authors.Select(a => a.Name)).Distinct()),
                b.Rating,
                Genres = b.Works.SelectMany(w => w.Genres).Select(g => g.Name).Distinct().ToList()
            })
            .ToListAsync(ct);

        var booksText = allBooks.Count > 0
            ? string.Join("\n", allBooks.Select(b => $"- \"{b.Title}\" by {b.Author} | {b.Rating}/5 | {string.Join(", ", b.Genres)}"))
            : "Library is empty.";

        var responseText = await CallWithSystemAsync(SharedParsers.BuildAdvisorSystemPrompt(booksText),
            $"I'm considering: {query}\n\nShould I get it? Respond with JSON only.", 2048, ct);
        return SharedParsers.ParseBookAdvisor(responseText);
    }

    private async Task<string> CallAsync(List<ChatMessage> messages, int maxTokens, CancellationToken ct)
    {
        var chatOptions = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
        var response = await GetClient().CompleteChatAsync(messages, chatOptions, ct);
        CallCount++;
        return response.Value.Content[0].Text ?? "";
    }

    private async Task<string> CallWithSystemAsync(string systemPrompt, string userMessage, int maxTokens, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        return await CallAsync(messages, maxTokens, ct);
    }
}
