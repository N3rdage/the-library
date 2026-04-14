using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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

            return new BookLookupResult(
                Isbn: isbn,
                Title: book.Title,
                Subtitle: book.Subtitle,
                Author: book.Authors?.FirstOrDefault()?.Name,
                Publisher: book.Publishers?.FirstOrDefault()?.Name,
                GenreCandidates: genres,
                DatePrinted: ParseLooseDate(book.PublishDate),
                CoverUrl: book.Cover?.Large ?? book.Cover?.Medium ?? $"https://covers.openlibrary.org/b/isbn/{isbn}-L.jpg",
                Source: "Open Library");
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

            return new BookLookupResult(
                Isbn: isbn,
                Title: item.Title,
                Subtitle: item.Subtitle,
                Author: item.Authors?.FirstOrDefault(),
                Publisher: item.Publisher,
                GenreCandidates: genres,
                DatePrinted: ParseLooseDate(item.PublishedDate),
                CoverUrl: cover,
                Source: "Google Books");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google Books lookup failed for ISBN {Isbn}", isbn);
            return null;
        }
    }

    private static string? CleanGenre(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 80)
        {
            return null;
        }
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static DateOnly? ParseLooseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (DateOnly.TryParse(raw, out var d))
        {
            return d;
        }
        if (int.TryParse(raw[..Math.Min(4, raw.Length)], out var year) && year is >= 1400 and <= 2999)
        {
            return new DateOnly(year, 1, 1);
        }
        return null;
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
}
