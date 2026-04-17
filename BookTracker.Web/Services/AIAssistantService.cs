using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BookTracker.Web.Services;

public class AIAssistantService(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IOptions<AIAssistantOptions> options) : IAIAssistantService
{
    private readonly AIAssistantOptions _options = options.Value;
    private AnthropicClient? _client;

    public int CallCount { get; private set; }

    private AnthropicClient GetClient()
    {
        _client ??= new AnthropicClient(_options.ApiKey);
        return _client;
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
- Include parent genres when suggesting sub-genres (e.g. if suggesting ""High (Epic) Fantasy"", also include ""Fantasy"").
- Only use exact genre names from the list above.
- Respond with valid JSON in this exact format:
{{
  ""genres"": [""Genre 1"", ""Genre 2""],
  ""reasoning"": ""Brief explanation of why these genres fit.""
}}";

        var userMessage = $@"Suggest genres for this book:
{bookDescription}

{currentGenresText}

Respond with JSON only.";

        var messages = new List<Message>
        {
            new(RoleType.User, userMessage)
        };

        var parameters = new MessageParameters
        {
            Messages = messages,
            MaxTokens = _options.MaxTokens,
            Model = _options.FastModel,
            Stream = false,
            System = new List<SystemMessage>
            {
                new(systemPrompt, new CacheControl { Type = CacheControlType.ephemeral })
            },
            PromptCaching = PromptCacheType.FineGrained
        };

        var client = GetClient();
        var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
        CallCount++;

        var responseText = response.Message?.ToString() ?? "";

        return ParseGenreSuggestion(responseText, allGenres.Select(g => g.Name).ToHashSet());
    }

    public async Task<CollectionSuggestionResult> SuggestCollectionsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Load books not in any series
        var uncategorised = await db.Books
            .Where(b => b.SeriesId == null)
            .OrderBy(b => b.Author)
            .ThenBy(b => b.Title)
            .Select(b => new { b.Title, b.Author })
            .ToListAsync(ct);

        if (uncategorised.Count == 0)
            return new CollectionSuggestionResult([], "All books are already in a series or collection.");

        // Load existing series for context
        var existingSeries = await db.Series
            .OrderBy(s => s.Name)
            .Select(s => new { s.Name, s.Type, BookCount = s.Books.Count })
            .ToListAsync(ct);

        var booksText = string.Join("\n", uncategorised.Select(b => $"- \"{b.Title}\" by {b.Author}"));
        var seriesText = existingSeries.Count > 0
            ? "Existing series/collections:\n" + string.Join("\n", existingSeries.Select(s => $"- {s.Name} ({s.Type}, {s.BookCount} books)"))
            : "No existing series or collections.";

        var systemPrompt = $@"You are a librarian helping organise a book collection into series and collections.

A ""Series"" is a numbered sequence with a known order (e.g. Harry Potter 1-7).
A ""Collection"" is a loose grouping by the same author or theme (e.g. all Agatha Christie's Poirot novels).

{seriesText}

Rules:
- Only suggest groupings where 2+ books from the list below belong together.
- Suggest whether each grouping is a ""Series"" or ""Collection"".
- If books could belong to an existing series/collection listed above, mention that.
- Don't suggest single-book groupings.
- Respond with valid JSON in this exact format:
{{
  ""groupings"": [
    {{
      ""name"": ""Suggested Series/Collection Name"",
      ""type"": ""Series"" or ""Collection"",
      ""reasoning"": ""Why these books belong together."",
      ""books"": [""Book Title 1"", ""Book Title 2""]
    }}
  ],
  ""summary"": ""Brief overall assessment of the collection.""
}}";

        var userMessage = $@"Here are the books not currently in any series or collection:

{booksText}

Suggest how these could be grouped. Respond with JSON only.";

        var messages = new List<Message>
        {
            new(RoleType.User, userMessage)
        };

        var parameters = new MessageParameters
        {
            Messages = messages,
            MaxTokens = 2048,
            Model = _options.FastModel,
            Stream = false,
            System = new List<SystemMessage>
            {
                new(systemPrompt, new CacheControl { Type = CacheControlType.ephemeral })
            },
            PromptCaching = PromptCacheType.FineGrained
        };

        var client = GetClient();
        var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
        CallCount++;

        var responseText = response.Message?.ToString() ?? "";
        return ParseCollectionSuggestion(responseText);
    }

    private static GenreSuggestionResult ParseGenreSuggestion(string responseText, HashSet<string> validGenres)
    {
        try
        {
            var json = StripCodeFences(responseText);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var genres = new List<string>();
            if (root.TryGetProperty("genres", out var genresElement))
            {
                foreach (var genre in genresElement.EnumerateArray())
                {
                    var name = genre.GetString();
                    if (name is not null && validGenres.Contains(name))
                        genres.Add(name);
                }
            }

            var reasoning = root.TryGetProperty("reasoning", out var reasoningElement)
                ? reasoningElement.GetString() ?? ""
                : "";

            return new GenreSuggestionResult(genres, reasoning);
        }
        catch
        {
            return new GenreSuggestionResult([], $"Could not parse AI response: {responseText[..Math.Min(200, responseText.Length)]}");
        }
    }

    private static CollectionSuggestionResult ParseCollectionSuggestion(string responseText)
    {
        try
        {
            var json = StripCodeFences(responseText);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var groupings = new List<CollectionGrouping>();
            if (root.TryGetProperty("groupings", out var groupingsElement))
            {
                foreach (var g in groupingsElement.EnumerateArray())
                {
                    var name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var type = g.TryGetProperty("type", out var t) ? t.GetString() ?? "Collection" : "Collection";
                    var reasoning = g.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
                    var books = new List<string>();
                    if (g.TryGetProperty("books", out var booksElement))
                    {
                        foreach (var b in booksElement.EnumerateArray())
                        {
                            var title = b.GetString();
                            if (title is not null) books.Add(title);
                        }
                    }
                    if (books.Count >= 2)
                        groupings.Add(new CollectionGrouping(name, type, reasoning, books));
                }
            }

            var summary = root.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString() ?? ""
                : "";

            return new CollectionSuggestionResult(groupings, summary);
        }
        catch
        {
            return new CollectionSuggestionResult([], $"Could not parse AI response: {responseText[..Math.Min(200, responseText.Length)]}");
        }
    }

    private static string StripCodeFences(string text)
    {
        var json = text.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```");
            if (firstNewline > 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }
        return json;
    }
}
