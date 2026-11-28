using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using BookTracker.Application;
using BookTracker.Application.Ai;
using BookTracker.Application.Series;
using OpenAI.Chat;

namespace BookTracker.Web.Services;

/// <summary>
/// AI assistant implementation using Azure OpenAI (GPT-4o).
/// </summary>
public class AzureOpenAIAssistantService(
    IDispatcher dispatcher,
    AzureOpenAIOptions options,
    ILogger<AzureOpenAIAssistantService> logger) : IAIAssistantService
{
    private readonly HttpClient? _httpClient;
    private ChatClient? _client;
    private bool _usageMissingWarned;

    /// <summary>Test seam: routes the SDK through a caller-supplied <see cref="HttpClient"/>.</summary>
    internal AzureOpenAIAssistantService(
        IDispatcher dispatcher, AzureOpenAIOptions options, ILogger<AzureOpenAIAssistantService> logger, HttpClient httpClient)
        : this(dispatcher, options, logger)
    {
        _httpClient = httpClient;
    }

    public int CallCount { get; private set; }

    private ChatClient GetClient()
    {
        if (_client is null)
        {
            var clientOptions = new AzureOpenAIClientOptions();
            if (_httpClient is not null)
                clientOptions.Transport = new HttpClientPipelineTransport(_httpClient);
            var azureClient = new AzureOpenAIClient(
                new Uri(options.Endpoint),
                new ApiKeyCredential(options.ApiKey),
                clientOptions);
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

        var response = await CallAsync(messages, 50, "ExtractIsbn", ct);
        var responseText = response.Trim();

        if (responseText.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return null;

        var cleaned = new string(responseText.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray());
        return cleaned.Length is 10 or 13 ? cleaned : null;
    }

    public async Task<BookPhotoReading?> ReadBookFromImageAsync(string base64Jpeg, CancellationToken ct = default)
    {
        var imageContent = ChatMessageContentPart.CreateImagePart(
            BinaryData.FromBytes(Convert.FromBase64String(base64Jpeg)), "image/jpeg");
        var textContent = ChatMessageContentPart.CreateTextPart(SharedParsers.BookPhotoPrompt);

        var responseText = await CallAsync([new UserChatMessage(imageContent, textContent)], 1024, "ReadBookFromImage", ct);
        return SharedParsers.ParseBookPhotoReading(responseText);
    }

    public async Task<GenreSuggestionResult> SuggestGenresAsync(
        string title, string author, string? subtitle,
        IReadOnlyList<string> currentGenres, CancellationToken ct = default)
    {
        var allGenres = await dispatcher.Query(new GetGenreTaxonomy(), ct);
        var taxonomyText = AiPromptContext.GenreTaxonomy(allGenres);

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

        var responseText = await CallWithSystemAsync(systemPrompt, userMessage, options.MaxTokens, "SuggestGenres", ct);
        return SharedParsers.ParseGenreSuggestion(responseText, allGenres.Select(g => g.Name).ToHashSet());
    }

    public async Task<CollectionSuggestionResult> SuggestCollectionsAsync(CancellationToken ct = default)
    {
        var uncategorised = await dispatcher.Query(new GetUnseriesedBooksForAi(), ct);

        if (uncategorised.Count == 0)
            return new CollectionSuggestionResult([], "All books are already in a series or collection.");

        var existingSeries = await dispatcher.Query(new GetSeriesList(null, null), ct);

        var booksText = AiPromptContext.UnseriesedBooks(uncategorised);
        var seriesText = AiPromptContext.ExistingSeries(existingSeries);

        var responseText = await CallWithSystemAsync(SharedParsers.BuildCollectionSystemPrompt(seriesText),
            $"Here are the books not in any series:\n\n{booksText}\n\nSuggest groupings. Respond with JSON only.", 2048, "SuggestCollections", ct);
        return SharedParsers.ParseCollectionSuggestion(responseText);
    }

    public async Task<ShoppingSuggestionResult> SuggestShoppingListAsync(CancellationToken ct = default)
    {
        var context = AiPromptContext.LibraryProfile(await dispatcher.Query(new GetAiLibraryProfile(), ct));

        var responseText = await CallWithSystemAsync(SharedParsers.ShoppingSystemPrompt,
            $"{context}\n\nSuggest 5-10 books to look for. Respond with JSON only.", 2048, "SuggestShoppingList", ct);
        return SharedParsers.ParseShoppingSuggestion(responseText);
    }

    public async Task<BookAdvisorResult> AssessBookAsync(string query, CancellationToken ct = default)
    {
        var booksText = AiPromptContext.AdvisorBooks(await dispatcher.Query(new GetLibraryProfileForAdvisor(), ct));

        var responseText = await CallWithSystemAsync(SharedParsers.BuildAdvisorSystemPrompt(booksText),
            $"I'm considering: {query}\n\nShould I get it? Respond with JSON only.", 2048, "AssessBook", ct);
        return SharedParsers.ParseBookAdvisor(responseText);
    }

    private async Task<string> CallAsync(List<ChatMessage> messages, int maxTokens, string operation, CancellationToken ct)
    {
        var chatOptions = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
        var response = await GetClient().CompleteChatAsync(messages, chatOptions, ct);
        CallCount++;
        await RecordUsageAsync(operation, response.Value, ct);
        return response.Value.Content[0].Text ?? "";
    }

    private async Task<string> CallWithSystemAsync(string systemPrompt, string userMessage, int maxTokens, string operation, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
        };

        return await CallAsync(messages, maxTokens, operation, ct);
    }

    private async Task RecordUsageAsync(string operation, ChatCompletion completion, CancellationToken ct)
    {
        if (completion.Usage is null)
        {
            if (!_usageMissingWarned)
            {
                _usageMissingWarned = true;
                logger.LogWarning("Azure OpenAI response for {Operation} carried no usage block; token usage is not recorded for this service instance's calls without one", operation);
            }
            return;
        }

        try
        {
            // The deployment name stands in for the model: it is what the
            // call is addressed to and what Azure bills by.
            await dispatcher.Send(new RecordAiUsage(
                nameof(AIProvider.AzureOpenAI), options.Deployment, operation,
                completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount), ct);
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: the AI call already succeeded and its
            // answer is in hand, so a bookkeeping failure must not fail it.
            logger.LogWarning(ex, "Recording AI usage for {Operation} on {Deployment} failed; the call's result is unaffected", operation, options.Deployment);
        }
    }
}
