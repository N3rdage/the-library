using System.Text.Json;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

/// <summary>
/// Shared JSON parsing and prompt building used by all AI provider implementations.
/// </summary>
internal static class SharedParsers
{
    public static GenreSuggestionResult ParseGenreSuggestion(string responseText, HashSet<string> validGenres)
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

            var reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
            return new GenreSuggestionResult(genres, reasoning);
        }
        catch
        {
            return new GenreSuggestionResult([], $"Could not parse AI response: {responseText[..Math.Min(200, responseText.Length)]}");
        }
    }

    public static CollectionSuggestionResult ParseCollectionSuggestion(string responseText)
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
                    var books = ParseStringArray(g, "books");
                    if (books.Count >= 2)
                        groupings.Add(new CollectionGrouping(name, type, reasoning, books));
                }
            }

            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            return new CollectionSuggestionResult(groupings, summary);
        }
        catch
        {
            return new CollectionSuggestionResult([], $"Could not parse AI response: {responseText[..Math.Min(200, responseText.Length)]}");
        }
    }

    public static ShoppingSuggestionResult ParseShoppingSuggestion(string responseText)
    {
        try
        {
            var json = StripCodeFences(responseText);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var recommendations = new List<BookRecommendation>();
            if (root.TryGetProperty("recommendations", out var recsElement))
            {
                foreach (var r in recsElement.EnumerateArray())
                {
                    var title = r.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var author = r.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "";
                    var reasoning = r.TryGetProperty("reasoning", out var re) ? re.GetString() ?? "" : "";
                    var seriesContext = r.TryGetProperty("series_context", out var sc) && sc.ValueKind != JsonValueKind.Null
                        ? sc.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(title))
                        recommendations.Add(new BookRecommendation(title, author, reasoning, seriesContext));
                }
            }

            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            return new ShoppingSuggestionResult(recommendations, summary);
        }
        catch
        {
            return new ShoppingSuggestionResult([], $"Could not parse AI response: {responseText[..Math.Min(200, responseText.Length)]}");
        }
    }

    public static BookAdvisorResult ParseBookAdvisor(string responseText)
    {
        try
        {
            var json = StripCodeFences(responseText);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var verdict = root.TryGetProperty("verdict", out var v) ? v.GetString() ?? "" : "";
            var score = root.TryGetProperty("suitability_score", out var s) ? s.GetInt32() : 5;
            var analysis = root.TryGetProperty("analysis", out var a) ? a.GetString() ?? "" : "";
            var pros = ParseStringArray(root, "pros");
            var cons = ParseStringArray(root, "cons");
            var similar = ParseStringArray(root, "similar_in_library");

            return new BookAdvisorResult(verdict, Math.Clamp(score, 1, 10), analysis, pros, cons, similar);
        }
        catch
        {
            return new BookAdvisorResult("Could not parse AI response", 5,
                responseText[..Math.Min(200, responseText.Length)], [], [], []);
        }
    }

    public static string StripCodeFences(string text)
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

    public static List<string> ParseStringArray(JsonElement root, string propertyName)
    {
        var list = new List<string>();
        if (root.TryGetProperty(propertyName, out var element))
        {
            foreach (var item in element.EnumerateArray())
            {
                var val = item.GetString();
                if (val is not null) list.Add(val);
            }
        }
        return list;
    }

    // Shared prompt templates

    public static string BuildCollectionSystemPrompt(string seriesText) => $@"You are a librarian helping organise a book collection into series and collections.

A ""Series"" is a numbered sequence with a known order (e.g. Harry Potter 1-7).
A ""Collection"" is a loose grouping by the same author or theme (e.g. all Agatha Christie's Poirot novels).

{seriesText}

Rules:
- Only suggest groupings where 2+ books from the list below belong together.
- Suggest whether each grouping is a ""Series"" or ""Collection"".
- If books could belong to an existing series/collection listed above, mention that.
- Don't suggest single-book groupings.
- Respond with valid JSON: {{""groupings"": [{{""name"": ""Name"", ""type"": ""Series"", ""reasoning"": ""Why."", ""books"": [""Title""]}}], ""summary"": ""Brief assessment.""}}";

    public const string ShoppingSystemPrompt = @"You are a book recommendation engine for a personal library. Based on the reader's collection, suggest books they should look for.

Rules:
- Prioritise filling gaps in incomplete series.
- Suggest books by authors the reader already enjoys.
- Suggest books in genres they read most.
- Include a mix: some series completions, some new discoveries.
- Suggest 5-10 books total.
- Respond with valid JSON: {""recommendations"": [{""title"": ""Title"", ""author"": ""Author"", ""reasoning"": ""Why."", ""series_context"": ""Part of [Series] #N"" or null}], ""summary"": ""Brief assessment.""}";

    public static string BuildAdvisorSystemPrompt(string booksText) => $@"You are a knowledgeable book advisor for a personal library. The reader has the following collection:

{booksText}

Your job: when the reader asks about a specific book or author, assess how well it fits their reading taste based on their library.

Rules:
- Consider genre overlap, author familiarity, rating patterns, and reading breadth.
- Be honest — if the book is outside their usual taste, say so, but note if that's a positive.
- Give a suitability score from 1 (poor fit) to 10 (perfect fit).
- Mention similar books already in their library.
- Respond with valid JSON: {{""verdict"": ""One sentence."", ""suitability_score"": 8, ""analysis"": ""Details."", ""pros"": [""Reason""], ""cons"": [""Reason""], ""similar_in_library"": [""Title""]}}";

    public static async Task<string> BuildLibraryContextAsync(BookTrackerDbContext db, CancellationToken ct)
    {
        // Author stats come from Works — author-per-work is the model now,
        // and a compendium contributes one entry per contained work to the
        // author's tally, which matches the reader's reading experience.
        var topAuthors = await db.Works
            .GroupBy(w => w.Author)
            .Select(g => new
            {
                Author = g.Key,
                Count = g.Count(),
                AvgRating = g.SelectMany(w => w.Books).Average(b => (double?)b.Rating) ?? 0.0
            })
            .OrderByDescending(a => a.Count)
            .Take(15)
            .ToListAsync(ct);

        var topGenres = await db.Genres
            .Select(g => new { g.Name, Count = g.Works.Count })
            .Where(g => g.Count > 0)
            .OrderByDescending(g => g.Count)
            .Take(10)
            .ToListAsync(ct);

        var highlyRated = await db.Books
            .Include(b => b.Works)
            .Where(b => b.Rating >= 4)
            .OrderByDescending(b => b.Rating).ThenBy(b => b.Title)
            .Take(20)
            .Select(b => new
            {
                b.Title,
                Author = string.Join(", ", b.Works.Select(w => w.Author).Distinct()),
                b.Rating
            })
            .ToListAsync(ct);

        var incompleteSeries = await db.Series
            .Include(s => s.Works)
            .Where(s => s.Type == SeriesType.Series && s.ExpectedCount != null)
            .ToListAsync(ct);

        var gapsText = incompleteSeries
            .Where(s => s.Works.Count < s.ExpectedCount!.Value)
            .Select(s => $"- {s.Name} by {s.Author ?? "various"}: have {s.Works.Count}/{s.ExpectedCount!.Value}, missing: {string.Join(", ", Enumerable.Range(1, s.ExpectedCount.Value).Where(i => !s.Works.Any(w => w.SeriesOrder == i)))}")
            .ToList();

        return $@"Here's the reader's library profile:

Top authors:
{string.Join("\n", topAuthors.Select(a => $"- {a.Author}: {a.Count} books, avg rating {a.AvgRating:F1}/5"))}

Top genres:
{string.Join("\n", topGenres.Select(g => $"- {g.Name}: {g.Count} books"))}

Highly rated books (4-5 stars):
{string.Join("\n", highlyRated.Select(b => $"- \"{b.Title}\" by {b.Author} ({b.Rating}/5)"))}

Incomplete series (gaps to fill):
{(gapsText.Count > 0 ? string.Join("\n", gapsText) : "No incomplete series.")}";
    }
}
