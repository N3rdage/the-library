using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

public class BookLookupService(HttpClient http, ILogger<BookLookupService> logger) : IBookLookupService
{
    public async Task<BookLookupResult?> LookupByIsbnAsync(string isbn, CancellationToken ct)
    {
        var cleanIsbn = new string(isbn.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cleanIsbn.Length is not (10 or 13))
        {
            return null;
        }

        return await TryOpenLibraryAsync(cleanIsbn, ct)
            ?? await TryGoogleBooksAsync(cleanIsbn, ct);
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
                DatePrintedPrecision: olPrecision);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open Library lookup failed for ISBN {Isbn}", isbn);
            return null;
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

    private static string? CleanGenre(string raw) => GenreCandidateCleaner.Clean(raw);

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
}
