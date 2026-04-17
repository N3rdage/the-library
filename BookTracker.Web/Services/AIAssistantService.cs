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

    private static GenreSuggestionResult ParseGenreSuggestion(string responseText, HashSet<string> validGenres)
    {
        try
        {
            var json = responseText.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    json = json[(firstNewline + 1)..lastFence].Trim();
            }

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
}
