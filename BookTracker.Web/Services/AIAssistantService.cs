using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using BookTracker.Application;
using BookTracker.Application.Ai;
using BookTracker.Application.Series;

namespace BookTracker.Web.Services;

/// <summary>
/// AI assistant implementation using the Anthropic API directly.
/// </summary>
public class AnthropicAIAssistantService(
    IDispatcher dispatcher,
    AnthropicOptions options,
    ILogger<AnthropicAIAssistantService> logger) : IAIAssistantService
{
    private AnthropicClient? _client;
    private bool _usageMissingWarned;

    /// <summary>Test seam: routes the SDK through a caller-supplied <see cref="HttpClient"/>.</summary>
    internal AnthropicAIAssistantService(
        IDispatcher dispatcher, AnthropicOptions options, ILogger<AnthropicAIAssistantService> logger, HttpClient httpClient)
        : this(dispatcher, options, logger)
    {
        _client = new AnthropicClient(options.ApiKey, httpClient);
    }

    public int CallCount { get; private set; }

    private AnthropicClient GetClient()
    {
        _client ??= new AnthropicClient(options.ApiKey);
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
                        Source = new ImageSource
                        {
                            MediaType = "image/jpeg",
                            Data = base64Jpeg
                        }
                    },
                    new TextContent
                    {
                        // Stronger X handling than the previous "include if present" —
                        // Drew was seeing X-ending ISBN-10s come back as 9 digits,
                        // which then failed the length check and returned null. The
                        // explicit example + "MUST include" phrasing leans on
                        // example-following rather than instruction-following, which
                        // tends to be more reliable.
                        Text = """
                            Read the ISBN from this book's image. Reply with just the ISBN — no other text, no explanation, no formatting.

                            ISBNs come in two forms:
                              - ISBN-13: 13 digits (e.g. 9780123456786)
                              - ISBN-10: 10 characters where the final character is a check digit that may be the letter X (e.g. 012345678X)

                            If the printed ISBN ends in X, you MUST include the X — without it the ISBN is invalid. Do not normalize, simplify, or 'correct' the ISBN; return exactly what is printed (digits only, no hyphens or spaces).

                            If no ISBN is visible, reply with NONE.
                            """
                    }
                }
            }
        };

        var response = await CallAsync(messages, options.FastModel, 50, "ExtractIsbn", ct);
        var responseText = (response.Message?.ToString() ?? "").Trim();

        if (responseText.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("ISBN OCR: model returned NONE (no ISBN found in image)");
            return null;
        }

        var cleaned = new string(responseText.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray());
        var accepted = cleaned.Length is 10 or 13;

        // Trace both raw and cleaned so post-deploy debugging can tell whether
        // a rejection (accepted=false) was a model dropping the X check digit
        // (raw "012345678" length 9), the model adding stray text (raw with
        // long prefix), or a genuine "couldn't read" case.
        logger.LogInformation(
            "ISBN OCR: raw={Raw} cleaned={Cleaned} length={Length} accepted={Accepted}",
            responseText, cleaned, cleaned.Length, accepted);

        return accepted ? cleaned : null;
    }

    public async Task<BookPhotoReading?> ReadBookFromImageAsync(string base64Jpeg, CancellationToken ct = default)
    {
        var messages = new List<Message>
        {
            new()
            {
                Role = RoleType.User,
                Content = new List<ContentBase>
                {
                    new ImageContent { Source = new ImageSource { MediaType = "image/jpeg", Data = base64Jpeg } },
                    new TextContent { Text = SharedParsers.BookPhotoPrompt }
                }
            }
        };

        var response = await CallAsync(messages, options.FastModel, 1024, "ReadBookFromImage", ct);
        return SharedParsers.ParseBookPhotoReading(response.Message?.ToString() ?? "");
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

        var response = await CallWithSystemAsync(systemPrompt, userMessage, options.FastModel, options.MaxTokens, "SuggestGenres", ct);
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

        var systemPrompt = SharedParsers.BuildCollectionSystemPrompt(seriesText);
        var userMessage = $"Here are the books not currently in any series or collection:\n\n{booksText}\n\nSuggest how these could be grouped. Respond with JSON only.";

        var response = await CallWithSystemAsync(systemPrompt, userMessage, options.FastModel, 2048, "SuggestCollections", ct);
        return SharedParsers.ParseCollectionSuggestion(response.Message?.ToString() ?? "");
    }

    public async Task<ShoppingSuggestionResult> SuggestShoppingListAsync(CancellationToken ct = default)
    {
        var context = AiPromptContext.LibraryProfile(await dispatcher.Query(new GetAiLibraryProfile(), ct));

        var response = await CallWithSystemAsync(SharedParsers.ShoppingSystemPrompt, $"{context}\n\nSuggest 5-10 books to look for. Respond with JSON only.", options.FastModel, 2048, "SuggestShoppingList", ct);
        return SharedParsers.ParseShoppingSuggestion(response.Message?.ToString() ?? "");
    }

    public async Task<BookAdvisorResult> AssessBookAsync(string query, CancellationToken ct = default)
    {
        var booksText = AiPromptContext.AdvisorBooks(await dispatcher.Query(new GetLibraryProfileForAdvisor(), ct));

        var systemPrompt = SharedParsers.BuildAdvisorSystemPrompt(booksText);
        var response = await CallWithSystemAsync(systemPrompt, $"I'm considering: {query}\n\nShould I get it? Respond with JSON only.", options.DeepModel, 2048, "AssessBook", ct);
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
                logger.LogWarning("Anthropic response for {Operation} carried no usage block; token usage is not recorded for this service instance's calls without one", operation);
            }
            return;
        }

        try
        {
            await dispatcher.Send(new RecordAiUsage(
                nameof(AIProvider.Anthropic), model, operation, response.Usage.InputTokens, response.Usage.OutputTokens), ct);
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: the AI call already succeeded and its
            // answer is in hand, so a bookkeeping failure must not fail it.
            logger.LogWarning(ex, "Recording AI usage for {Operation} on {Model} failed; the call's result is unaffected", operation, model);
        }
    }
}
