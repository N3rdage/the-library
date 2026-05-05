using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BookTracker.Data.Models;
using Microsoft.Extensions.Options;

namespace BookTracker.Web.Services;

public partial class BookLookupService(
    HttpClient http,
    ILogger<BookLookupService> logger,
    IOptions<TroveOptions> troveOptions) : IBookLookupService
{
    public async Task<BookLookupResult?> LookupByIsbnAsync(string isbn, CancellationToken ct)
    {
        var cleanIsbn = new string(isbn.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cleanIsbn.Length is not (10 or 13))
        {
            logger.LogInformation("ISBN lookup skipped — invalid length for {Isbn}", cleanIsbn);
            return null;
        }

        logger.LogInformation("ISBN lookup starting for {Isbn}", cleanIsbn);

        // Trove (National Library of Australia) sits last in the chain as a
        // coverage-of-last-resort for self-published / Australian titles that
        // Open Library and Google Books tend to miss. Skipped silently when
        // no API key is configured.
        var result = await TryOpenLibraryAsync(cleanIsbn, ct)
            ?? await TryGoogleBooksAsync(cleanIsbn, ct)
            ?? await TryTroveAsync(cleanIsbn, ct);

        if (result is null)
        {
            logger.LogWarning("ISBN lookup found no result from any provider for {Isbn}", cleanIsbn);
        }
        return result;
    }

    public async Task<IReadOnlyList<BookSearchCandidate>> SearchByTitleAuthorAsync(
        string? title, string? author, CancellationToken ct)
    {
        var t = title?.Trim() ?? "";
        var a = author?.Trim() ?? "";
        if (t.Length == 0 && a.Length == 0)
        {
            return [];
        }

        try
        {
            var query = new List<string>();
            if (t.Length > 0) query.Add($"title={Uri.EscapeDataString(t)}");
            if (a.Length > 0) query.Add($"author={Uri.EscapeDataString(a)}");
            // limit=10 keeps the UI manageable; fields= trims the response
            // payload (Open Library's default includes hundreds of fields
            // per row).
            query.Add("limit=10");
            query.Add("fields=key,title,author_name,first_publish_year,edition_count,cover_i");

            var url = $"https://openlibrary.org/search.json?{string.Join("&", query)}";
            var doc = await http.GetFromJsonAsync<OpenLibrarySearchResponse>(url, ct);
            if (doc?.Docs is null) return [];

            return doc.Docs
                .Where(d => !string.IsNullOrWhiteSpace(d.Title))
                .Select(d => new BookSearchCandidate(
                    WorkKey: d.Key ?? "",
                    Title: d.Title,
                    Author: d.AuthorName is { Count: > 0 } ? string.Join(", ", d.AuthorName) : null,
                    FirstPublishYear: d.FirstPublishYear,
                    EditionCount: d.EditionCount,
                    CoverUrl: d.CoverId is int id ? $"https://covers.openlibrary.org/b/id/{id}-M.jpg" : null,
                    OpenLibraryUrl: string.IsNullOrEmpty(d.Key) ? null : $"https://openlibrary.org{d.Key}/editions"))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open Library title/author search failed for title={Title} author={Author}", t, a);
            return [];
        }
    }

    private async Task<BookLookupResult?> TryOpenLibraryAsync(string isbn, CancellationToken ct)
    {
        try
        {
            var url = $"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=data";
            var doc = await http.GetFromJsonAsync<Dictionary<string, OpenLibraryBook>>(url, ct);
            if (doc is null || !doc.TryGetValue($"ISBN:{isbn}", out var book))
            {
                logger.LogDebug("Open Library returned no record for ISBN {Isbn}", isbn);
                return null;
            }

            var genres = (book.Subjects ?? [])
                .Select(s => s.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(CleanGenre)
                .Where(n => n is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            var (olDate, olPrecision) = ParseLooseDate(book.PublishDate);
            // Series lives in the `jscmd=details` view, not the `jscmd=data`
            // curated view we hit above — so this is a separate call. Failure
            // here is non-fatal: the result still returns with Series=null and
            // the suggestion banner falls back to local title/author matching.
            var (seriesName, seriesNumber, seriesNumberRaw) = await TryFetchOpenLibrarySeriesAsync(isbn, ct);
            return new BookLookupResult(
                Isbn: isbn,
                Title: book.Title,
                Subtitle: book.Subtitle,
                Author: book.Authors?.FirstOrDefault()?.Name,
                Publisher: book.Publishers?.FirstOrDefault()?.Name,
                GenreCandidates: genres,
                DatePrinted: olDate,
                CoverUrl: book.Cover?.Large ?? book.Cover?.Medium ?? $"https://covers.openlibrary.org/b/isbn/{isbn}-L.jpg",
                Source: "Open Library",
                Format: BookFormatNormalizer.Normalize(book.PhysicalFormat, book.PhysicalDimensions),
                DatePrintedPrecision: olPrecision,
                Series: seriesName,
                SeriesNumber: seriesNumber,
                SeriesNumberRaw: seriesNumberRaw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open Library lookup failed for ISBN {Isbn}", isbn);
            return null;
        }
    }

    private async Task<(string?, int?, string?)> TryFetchOpenLibrarySeriesAsync(string isbn, CancellationToken ct)
    {
        try
        {
            var url = $"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=details";
            var doc = await http.GetFromJsonAsync<Dictionary<string, OpenLibraryDetailsWrapper>>(url, ct);
            if (doc is null || !doc.TryGetValue($"ISBN:{isbn}", out var wrapper) || wrapper.Details is null)
            {
                return (null, null, null);
            }
            return ParseOpenLibrarySeries(wrapper.Details.Series);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open Library details lookup failed for ISBN {Isbn}", isbn);
            return (null, null, null);
        }
    }

    private async Task<BookLookupResult?> TryGoogleBooksAsync(string isbn, CancellationToken ct)
    {
        try
        {
            var url = $"https://www.googleapis.com/books/v1/volumes?q=isbn:{isbn}";
            var doc = await http.GetFromJsonAsync<GoogleBooksResponse>(url, ct);
            var item = doc?.Items?.FirstOrDefault()?.VolumeInfo;
            if (item is null)
            {
                logger.LogDebug("Google Books returned no record for ISBN {Isbn}", isbn);
                return null;
            }

            var genres = (item.Categories ?? [])
                .SelectMany(c => c.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(CleanGenre)
                .Where(n => n is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            var cover = item.ImageLinks?.Thumbnail?.Replace("http://", "https://")
                ?? item.ImageLinks?.SmallThumbnail?.Replace("http://", "https://");

            var (gbDate, gbPrecision) = ParseLooseDate(item.PublishedDate);
            return new BookLookupResult(
                Isbn: isbn,
                Title: item.Title,
                Subtitle: item.Subtitle,
                Author: item.Authors?.FirstOrDefault(),
                Publisher: item.Publisher,
                GenreCandidates: genres,
                DatePrinted: gbDate,
                CoverUrl: cover,
                Source: "Google Books",
                DatePrintedPrecision: gbPrecision);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Books lookup failed for ISBN {Isbn}", isbn);
            return null;
        }
    }

    private async Task<BookLookupResult?> TryTroveAsync(string isbn, CancellationToken ct)
    {
        var key = troveOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            logger.LogDebug("Trove skipped for ISBN {Isbn} — no API key configured", isbn);
            return null;
        }

        try
        {
            // include=all pulls in subject + contributor so we can map genres
            // and author. n=1 because we only care about the top match.
            var query = Uri.EscapeDataString($"isbn:{isbn}");
            var url = $"https://api.trove.nla.gov.au/v3/result?q={query}&category=book&encoding=json&n=1&include=all&key={Uri.EscapeDataString(key)}";
            var doc = await http.GetFromJsonAsync<TroveResponse>(url, ct);
            var work = doc?.Category?
                .FirstOrDefault(c => string.Equals(c.Code, "book", StringComparison.OrdinalIgnoreCase))?
                .Records?.Work?.FirstOrDefault();
            if (work is null || string.IsNullOrWhiteSpace(work.Title))
            {
                logger.LogDebug("Trove returned no record for ISBN {Isbn}", isbn);
                return null;
            }

            // Trove v3 returns multi-valued fields as arrays but occasionally
            // falls back to a single string when there's only one value — the
            // helper flattens both shapes.
            var author = TroveStringValues(work.Contributor).FirstOrDefault();
            var genres = TroveStringValues(work.Subject)
                .Select(CleanGenre)
                .Where(n => n is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            var (date, precision) = ParseLooseDate(work.Issued);

            return new BookLookupResult(
                Isbn: isbn,
                Title: work.Title,
                Subtitle: null,
                Author: author,
                Publisher: null,
                GenreCandidates: genres,
                DatePrinted: date,
                CoverUrl: null,
                Source: "Trove",
                DatePrintedPrecision: precision);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Trove lookup failed for ISBN {Isbn}", isbn);
            return null;
        }
    }

    private static IEnumerable<string> TroveStringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s)) yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var str = item.GetString();
                        if (!string.IsNullOrWhiteSpace(str)) yield return str;
                    }
                }
                break;
        }
    }

    private static string? CleanGenre(string raw) => GenreCandidateCleaner.Clean(raw);

    /// <summary>
    /// Parses Open Library's free-text `series` field into a name, integer
    /// order, and raw order string. Real-world examples:
    /// <list type="bullet">
    ///   <item><description><c>"Discworld"</c> → ("Discworld", null, null)</description></item>
    ///   <item><description><c>"Discworld -- 5"</c> → ("Discworld", 5, "5")</description></item>
    ///   <item><description><c>"Foundation series ; bk. 1"</c> → ("Foundation series", 1, "1")</description></item>
    ///   <item><description><c>"The Wheel of Time #5.5"</c> → ("The Wheel of Time", null, "5.5")</description></item>
    /// </list>
    /// Non-integer orders ("5.5", "1A") leave <see cref="BookLookupResult.SeriesNumber"/>
    /// null (the current model can only store integer order — see the follow-up
    /// TODO "Support non-integer / hierarchical SeriesOrder") but preserve the
    /// raw string so the suggestion message can surface it for manual entry.
    /// </summary>
    internal static (string? Name, int? Number, string? NumberRaw) ParseOpenLibrarySeries(IList<string>? series)
    {
        var raw = series?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return (null, null, null);

        // Pattern A: "Name <separator> <number>" where separator is one of
        // -- / # / ; / , optionally followed by a "bk."/"book"/"vol."/"volume"/"no."/"pt."
        // marker. Captures the trailing number — supports decimals so "5.5"
        // surfaces in the raw column without truncation.
        var separatorMatch = SeriesSeparatorRegex().Match(raw);
        if (separatorMatch.Success)
        {
            var name = separatorMatch.Groups[1].Value.Trim().TrimEnd(';', ',', '-').Trim();
            var rawNum = separatorMatch.Groups[2].Value;
            var intNum = int.TryParse(rawNum, out var n) ? n : (int?)null;
            return (string.IsNullOrWhiteSpace(name) ? null : name, intNum, rawNum);
        }

        // Pattern B: trailing space then number, e.g. "Discworld 5".
        var trailingMatch = SeriesTrailingNumberRegex().Match(raw);
        if (trailingMatch.Success)
        {
            var name = trailingMatch.Groups[1].Value.Trim();
            var rawNum = trailingMatch.Groups[2].Value;
            var intNum = int.TryParse(rawNum, out var n) ? n : (int?)null;
            return (string.IsNullOrWhiteSpace(name) ? null : name, intNum, rawNum);
        }

        // No number — full string is the series name.
        return (raw, null, null);
    }

    [GeneratedRegex(@"^(.+?)\s*(?:--|#|;\s*(?:bk\.?|book|vol\.?|volume|no\.?|pt\.?|part)?|,\s*(?:bk\.?|book|vol\.?|volume|no\.?|pt\.?|part)?)\s*(\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesSeparatorRegex();

    [GeneratedRegex(@"^(.+?)\s+(\d+(?:\.\d+)?)\s*$")]
    private static partial Regex SeriesTrailingNumberRegex();

    // Open Library / Google Books often only give a year ("1934") so we
    // route through the same partial-date parser used by the form inputs
    // — the precision is then carried alongside the DateOnly so the UI
    // doesn't render "1 Jan 1934" for a year-only source.
    private static (DateOnly?, DatePrecision) ParseLooseDate(string? raw)
    {
        var pd = PartialDateParser.TryParse(raw);
        if (pd is { Date: DateOnly d }) return (d, pd.Precision);

        // Last-resort year fallback: many records have noise like "1934, c1932"
        // — try the leading 4 digits.
        if (!string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw[..Math.Min(4, raw.Length)], out var year)
            && year is >= 1400 and <= 2999)
        {
            return (new DateOnly(year, 1, 1), DatePrecision.Year);
        }

        return (null, DatePrecision.Day);
    }

    private sealed class OpenLibraryBook
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("subtitle")] public string? Subtitle { get; set; }
        [JsonPropertyName("authors")] public List<OpenLibraryAuthor>? Authors { get; set; }
        [JsonPropertyName("publishers")] public List<OpenLibraryPublisher>? Publishers { get; set; }
        [JsonPropertyName("subjects")] public List<OpenLibrarySubject>? Subjects { get; set; }
        [JsonPropertyName("publish_date")] public string? PublishDate { get; set; }
        [JsonPropertyName("cover")] public OpenLibraryCover? Cover { get; set; }
        [JsonPropertyName("physical_format")] public string? PhysicalFormat { get; set; }
        [JsonPropertyName("physical_dimensions")] public string? PhysicalDimensions { get; set; }
    }

    // The `jscmd=details` view wraps the Edition's raw record under `details`.
    // Used only for the `series` field — everything else parses from the
    // friendlier `jscmd=data` shape (OpenLibraryBook above).
    private sealed class OpenLibraryDetailsWrapper
    {
        [JsonPropertyName("details")] public OpenLibraryDetails? Details { get; set; }
    }
    private sealed class OpenLibraryDetails
    {
        // Free-text strings — entries like "Discworld", "Discworld -- 5",
        // or "Foundation series ; bk. 1". Parsed via ParseOpenLibrarySeries.
        [JsonPropertyName("series")] public List<string>? Series { get; set; }
    }
    private sealed class OpenLibraryAuthor { [JsonPropertyName("name")] public string? Name { get; set; } }
    private sealed class OpenLibraryPublisher { [JsonPropertyName("name")] public string? Name { get; set; } }
    private sealed class OpenLibrarySubject { [JsonPropertyName("name")] public string Name { get; set; } = ""; }
    private sealed class OpenLibraryCover
    {
        [JsonPropertyName("small")] public string? Small { get; set; }
        [JsonPropertyName("medium")] public string? Medium { get; set; }
        [JsonPropertyName("large")] public string? Large { get; set; }
    }

    private sealed class GoogleBooksResponse { [JsonPropertyName("items")] public List<GoogleBooksItem>? Items { get; set; } }
    private sealed class GoogleBooksItem { [JsonPropertyName("volumeInfo")] public GoogleVolumeInfo? VolumeInfo { get; set; } }
    private sealed class GoogleVolumeInfo
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("subtitle")] public string? Subtitle { get; set; }
        [JsonPropertyName("authors")] public List<string>? Authors { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("categories")] public List<string>? Categories { get; set; }
        [JsonPropertyName("publishedDate")] public string? PublishedDate { get; set; }
        [JsonPropertyName("imageLinks")] public GoogleImageLinks? ImageLinks { get; set; }
    }
    private sealed class GoogleImageLinks
    {
        [JsonPropertyName("smallThumbnail")] public string? SmallThumbnail { get; set; }
        [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
    }

    private sealed class OpenLibrarySearchResponse
    {
        [JsonPropertyName("docs")] public List<OpenLibrarySearchDoc>? Docs { get; set; }
    }
    private sealed class OpenLibrarySearchDoc
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("author_name")] public List<string>? AuthorName { get; set; }
        [JsonPropertyName("first_publish_year")] public int? FirstPublishYear { get; set; }
        [JsonPropertyName("edition_count")] public int? EditionCount { get; set; }
        [JsonPropertyName("cover_i")] public int? CoverId { get; set; }
    }

    private sealed class TroveResponse
    {
        [JsonPropertyName("category")] public List<TroveCategory>? Category { get; set; }
    }
    private sealed class TroveCategory
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("records")] public TroveRecords? Records { get; set; }
    }
    private sealed class TroveRecords
    {
        [JsonPropertyName("total")] public int Total { get; set; }
        [JsonPropertyName("work")] public List<TroveWork>? Work { get; set; }
    }
    private sealed class TroveWork
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("issued")] public string? Issued { get; set; }
        // contributor / subject arrive as either a single string or an array;
        // JsonElement lets TroveStringValues normalise both shapes.
        [JsonPropertyName("contributor")] public JsonElement Contributor { get; set; }
        [JsonPropertyName("subject")] public JsonElement Subject { get; set; }
    }
}
