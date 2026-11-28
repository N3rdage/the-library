using System.Text.Json;
using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

/// <summary>
/// Shared JSON parsing and prompt templates used by all AI provider implementations.
/// The library text the templates carry comes from <see cref="AiPromptContext"/>.
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

    /// <summary>
    /// Parses the model's answer to <see cref="BookPhotoPrompt"/>. Returns null for
    /// malformed JSON, an unreadable-book answer, or an answer with no title and no
    /// contributor. A missing field is null; a missing confidence is 0; confidences
    /// are clamped to 0..1; an unrecognised role becomes Contributor (Author would
    /// feed the author rollups with a guess); an ISBN that is not 10 or 13 digits
    /// (the last may be X) is dropped.
    /// </summary>
    public static BookPhotoReading? ParseBookPhotoReading(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripCodeFences(responseText));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (root.TryGetProperty("readable", out var readable) && readable.ValueKind == JsonValueKind.False)
                return null;

            var title = ReadPhotoString(root, "title");
            var contributors = new List<PhotoContributor>();
            if (root.TryGetProperty("contributors", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in list.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object) continue;
                    var name = ReadString(c, "name");
                    if (name is null) continue;
                    var roleText = ReadString(c, "role");
                    var role = roleText is not null
                        && Enum.TryParse<AuthorRole>(roleText, ignoreCase: true, out var parsed)
                        && Enum.IsDefined(parsed)
                        ? parsed : AuthorRole.Contributor;
                    contributors.Add(new PhotoContributor(name, role, ReadConfidence(c)));
                }
            }

            if (title is null && contributors.Count == 0)
                return null;

            var isbn = ReadPhotoString(root, "isbn");
            if (isbn is not null)
            {
                var cleaned = new string(isbn.Value.Where(ch => char.IsDigit(ch) || ch is 'X' or 'x').ToArray()).ToUpperInvariant();
                var valid = cleaned.Length is 10 or 13 && cleaned[..^1].All(char.IsDigit);
                isbn = valid ? new PhotoField<string>(cleaned, isbn.Confidence) : null;
            }

            var reference = new ReferenceWorkHint(false, null, 0);
            if (root.TryGetProperty("reference_work", out var r) && r.ValueKind == JsonValueKind.Object)
            {
                var flag = r.TryGetProperty("is_reference_work", out var f) && f.ValueKind == JsonValueKind.True;
                reference = new ReferenceWorkHint(flag, flag ? ReadString(r, "kind") : null, ReadConfidence(r));
            }

            return new BookPhotoReading(
                title, ReadPhotoString(root, "subtitle"), contributors, ReadPhotoString(root, "publisher"),
                ReadPhotoInt(root, "year"), ReadPhotoInt(root, "edition_number"), isbn, reference);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.String) return null;
        var text = e.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static double ReadConfidence(JsonElement obj) =>
        obj.TryGetProperty("confidence", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d)
            ? Math.Clamp(d, 0.0, 1.0) : 0.0;

    private static PhotoField<string>? ReadPhotoString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Object) return null;
        var value = ReadString(field, "value");
        return value is null ? null : new PhotoField<string>(value, ReadConfidence(field));
    }

    private static PhotoField<int>? ReadPhotoInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Object) return null;
        if (!field.TryGetProperty("value", out var v)) return null;
        var ok = false;
        var number = 0;
        if (v.ValueKind == JsonValueKind.Number) ok = v.TryGetInt32(out number);
        else if (v.ValueKind == JsonValueKind.String) ok = int.TryParse(v.GetString(), out number);
        return ok && number > 0 ? new PhotoField<int>(number, ReadConfidence(field)) : null;
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

    public const string BookPhotoPrompt = """
        Read this photo of a book's cover or title page. Reply with JSON only, no other text, in exactly this shape:

        {"readable": true,
         "title": {"value": "Title", "confidence": 0.95},
         "subtitle": {"value": "Subtitle", "confidence": 0.8},
         "contributors": [{"name": "Full Name", "role": "Author", "confidence": 0.9}],
         "publisher": {"value": "Publisher", "confidence": 0.7},
         "year": {"value": 1998, "confidence": 0.6},
         "edition_number": {"value": 2, "confidence": 0.9},
         "isbn": {"value": "9780123456786", "confidence": 0.9},
         "reference_work": {"is_reference_work": true, "kind": "dictionary", "confidence": 0.8}}

        Rules:
        - confidence is 0.0 to 1.0: how sure you are that you read that value correctly.
        - Use null for any field the image does not show. Never guess a value that is not printed.
        - role is one of: Author, Editor, Translator, Illustrator, Adaptor, Compiler, Foreword, Contributor.
        - year is the publication or copyright year as a number. edition_number is the number only ("2nd edition" is 2).
        - isbn is digits only, without hyphens or spaces; an ISBN-10 ending in X keeps the X.
        - reference_work.is_reference_work is true for dictionaries, encyclopaedias, atlases, field guides, thesauruses, almanacs and similar; kind names which.
        - If the image does not show a book, reply {"readable": false}.
        """;

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
}
