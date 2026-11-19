using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using BookTracker.Application;
using BookTracker.Application.Ai;
using BookTracker.Application.Series;

namespace BookTracker.Web.Services;

/// <summary>
/// AI assistant implementation using Claude models hosted on Microsoft Foundry.
/// Uses the Anthropic SDK with a custom HttpClient pointing at the Azure endpoint.
/// </summary>
public class MicrosoftFoundryAIAssistantService(
    IDispatcher dispatcher,
    MicrosoftFoundryOptions options,
    ILogger<MicrosoftFoundryAIAssistantService> logger) : IAIAssistantService
{
    private AnthropicClient? _client;
    private bool _usageMissingWarned;

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

        var response = await CallAsync(messages, options.FastDeployment, 50, "ExtractIsbn", ct);
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

        var response = await CallWithSystemAsync(systemPrompt, userMessage, options.FastDeployment, options.MaxTokens, "SuggestGenres", ct);
        return SharedParsers.ParseGenreSuggestion(response.Message?.ToString() ?? "", allGenres.Select(g => g.Name).ToHashSet());
    }

    public async Task<CollectionSuggestionResult> SuggestCollectionsAsync(CancellationToken ct = default)
    {
        var uncategorised = await dispatcher.Query(new GetUnseriesedBooksForAi(), ct);

        if (uncategorised.Count == 0)
            return new CollectionSuggestionResult([], "All books are already in a series or collection.");

        var existingSeries = await dispatcher.Query(new GetSeriesList(null, null), ct);

        var booksText = AiPromptContext.UnseriesedBooks(uncategorised);
        var seriesText = AiPromptContext.ExistingSeries(existingSeries);

        var response = await CallWithSystemAsync(SharedParsers.BuildCollectionSystemPrompt(seriesText),
            $"Here are the books not in any series:\n\n{booksText}\n\nSuggest groupings. Respond with JSON only.",
            options.FastDeployment, 2048, "SuggestCollections", ct);
        return SharedParsers.ParseCollectionSuggestion(response.Message?.ToString() ?? "");
    }

    public async Task<ShoppingSuggestionResult> SuggestShoppingListAsync(CancellationToken ct = default)
    {
        var context = AiPromptContext.LibraryProfile(await dispatcher.Query(new GetAiLibraryProfile(), ct));

        var response = await CallWithSystemAsync(SharedParsers.ShoppingSystemPrompt,
            $"{context}\n\nSuggest 5-10 books to look for. Respond with JSON only.",
            options.FastDeployment, 2048, "SuggestShoppingList", ct);
        return SharedParsers.ParseShoppingSuggestion(response.Message?.ToString() ?? "");
    }

    public async Task<BookAdvisorResult> AssessBookAsync(string query, CancellationToken ct = default)
    {
        var booksText = AiPromptContext.AdvisorBooks(await dispatcher.Query(new GetLibraryProfileForAdvisor(), ct));

        var response = await CallWithSystemAsync(SharedParsers.BuildAdvisorSystemPrompt(booksText),
            $"I'm considering: {query}\n\nShould I get it? Respond with JSON only.",
            options.DeepDeployment, 2048, "AssessBook", ct);
        return SharedParsers.ParseBookAdvisor(response.Message?.ToString() ?? "");
    }

    private async Task<MessageResponse> CallAsync(List<Message> messages, string model, int maxTokens, string operation, CancellationToken ct)
    {
        var parameters = new MessageParameters { Messages = messages, MaxTokens = maxTokens, Model = model, Stream = false };
        var response = await GetClient().Messages.GetClaudeMessageAsync(parameters, ct);
        CallCount++;
        await RecordUsageAsync(model, operation, response, ct);
        return response;
    }

    private async Task<MessageResponse> CallWithSystemAsync(string systemPrompt, string userMessage, string model, int maxTokens, string operation, CancellationToken ct)
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
        await RecordUsageAsync(model, operation, response, ct);
        return response;
    }

    private async Task RecordUsageAsync(string model, string operation, MessageResponse response, CancellationToken ct)
    {
        if (response.Usage is null)
        {
            if (!_usageMissingWarned)
            {
                _usageMissingWarned = true;
                logger.LogWarning("Microsoft Foundry response for {Operation} carried no usage block; token usage is not recorded for this service instance's calls without one", operation);
            }
            return;
        }

        try
        {
            await dispatcher.Send(new RecordAiUsage(
                nameof(AIProvider.MicrosoftFoundry), model, operation, response.Usage.InputTokens, response.Usage.OutputTokens), ct);
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: the AI call already succeeded and its
            // answer is in hand, so a bookkeeping failure must not fail it.
            logger.LogWarning(ex, "Recording AI usage for {Operation} on {Model} failed; the call's result is unaffected", operation, model);
        }
    }
}
