using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

public interface IAIAssistantService
{
    /// <summary>
    /// Suggests genres for a book from the preset taxonomy.
    /// Returns genre names that match entries in the Genres table.
    /// </summary>
    Task<GenreSuggestionResult> SuggestGenresAsync(string title, string author, string? subtitle, IReadOnlyList<string> currentGenres, CancellationToken ct = default);

    /// <summary>
    /// Analyses uncategorised books and suggests series/collection groupings.
    /// </summary>
    Task<CollectionSuggestionResult> SuggestCollectionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Suggests books to look for based on incomplete series, reading preferences, and library patterns.
    /// </summary>
    Task<ShoppingSuggestionResult> SuggestShoppingListAsync(CancellationToken ct = default);

    /// <summary>
    /// Assesses whether a specific book or author would be a good fit for the reader
    /// based on their library, tastes, and reading patterns. Uses the deep model (Opus).
    /// </summary>
    Task<BookAdvisorResult> AssessBookAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Extracts an ISBN number from a photo using vision OCR.
    /// Returns the extracted ISBN or null if not found.
    /// </summary>
    Task<string?> ExtractIsbnFromImageAsync(string base64Jpeg, CancellationToken ct = default);

    /// <summary>
    /// Reads a book's metadata from a photo of its cover or title page, with a
    /// confidence per field. Returns null when the model cannot read a book from
    /// the image or its answer is unusable.
    /// </summary>
    Task<BookPhotoReading?> ReadBookFromImageAsync(string base64Jpeg, CancellationToken ct = default);

    /// <summary>Number of API calls made in this service instance's lifetime.</summary>
    int CallCount { get; }
}

/// <summary>A value read from a photo and how sure the model is of it (0.0 to 1.0).</summary>
public record PhotoField<T>(T Value, double Confidence);

/// <summary>A credited person read from a photo, with the role the model judged they hold.</summary>
public record PhotoContributor(string Name, AuthorRole Role, double Confidence);

/// <summary>
/// Whether the photographed book looks like a reference work (dictionary,
/// encyclopaedia, atlas, field guide, ...). <paramref name="Kind"/> names the
/// type when the model gave one.
/// </summary>
public record ReferenceWorkHint(bool IsReferenceWork, string? Kind, double Confidence);

/// <summary>
/// What the model read from a cover or title page photo. A field the image does
/// not show is null; <see cref="Contributors"/> is empty when none are credited.
/// </summary>
public record BookPhotoReading(
    PhotoField<string>? Title,
    PhotoField<string>? Subtitle,
    IReadOnlyList<PhotoContributor> Contributors,
    PhotoField<string>? Publisher,
    PhotoField<int>? Year,
    PhotoField<int>? EditionNumber,
    PhotoField<string>? Isbn,
    ReferenceWorkHint ReferenceWork);

public record GenreSuggestionResult(
    IReadOnlyList<string> SuggestedGenres,
    string Reasoning);

public record CollectionSuggestionResult(
    IReadOnlyList<CollectionGrouping> Groupings,
    string Summary);

public record CollectionGrouping(
    string SuggestedName,
    string Type,
    string Reasoning,
    IReadOnlyList<string> BookTitles);

public record ShoppingSuggestionResult(
    IReadOnlyList<BookRecommendation> Recommendations,
    string Summary);

public record BookRecommendation(
    string Title,
    string Author,
    string Reasoning,
    string? SeriesContext);

public record BookAdvisorResult(
    string Verdict,
    int SuitabilityScore,
    string Analysis,
    IReadOnlyList<string> Pros,
    IReadOnlyList<string> Cons,
    IReadOnlyList<string> SimilarInLibrary);
