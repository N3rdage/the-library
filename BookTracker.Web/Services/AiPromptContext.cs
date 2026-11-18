using BookTracker.Application.Ai;
using BookTracker.Application.Series;

namespace BookTracker.Web.Services;

/// <summary>
/// Turns the AI read models into the library text every provider puts in its
/// prompts. Pure functions: the providers dispatch the queries, these render
/// the result, so the three providers send the same facts in the same words.
/// </summary>
internal static class AiPromptContext
{
    // "- Fantasy" for a top-level genre, "  - Space Opera (sub-genre of Science
    // Fiction)" for a child.
    public static string GenreTaxonomy(IReadOnlyList<GenreTaxonomyEntry> genres) =>
        string.Join("\n", genres.Select(g =>
            g.ParentName is not null ? $"  - {g.Name} (sub-genre of {g.ParentName})" : $"- {g.Name}"));

    public static string UnseriesedBooks(IReadOnlyList<UnseriesedBook> books) =>
        string.Join("\n", books.Select(b => $"- \"{b.Title}\" by {b.Author}"));

    public static string ExistingSeries(IReadOnlyList<SeriesListItem> series) =>
        series.Count > 0
            ? "Existing series/collections:\n" + string.Join("\n", series.Select(s => $"- {s.Name} ({s.Type}, {s.BookCount} books)"))
            : "No existing series or collections.";

    public static string AdvisorBooks(IReadOnlyList<AdvisorBook> books) =>
        books.Count > 0
            ? string.Join("\n", books.Select(b => $"- \"{b.Title}\" by {b.Author} | {b.Rating}/5 | {string.Join(", ", b.Genres)}"))
            : "Library is empty.";

    public static string LibraryProfile(AiLibraryProfile profile)
    {
        var gapsText = profile.SeriesGaps
            .Select(g => $"- {g.SeriesName} by {g.Author ?? "various"}: have {g.OwnedCount}/{g.ExpectedCount}, missing: {string.Join(", ", g.MissingPositions)}")
            .ToList();

        return $@"Here's the reader's library profile:

Top authors:
{string.Join("\n", profile.TopAuthors.Select(a => $"- {a.Author}: {a.Count} books, avg rating {a.AvgRating:F1}/5"))}

Top genres:
{string.Join("\n", profile.TopGenres.Select(g => $"- {g.Name}: {g.Count} books"))}

Highly rated books (4-5 stars):
{string.Join("\n", profile.HighlyRated.Select(b => $"- \"{b.Title}\" by {b.Author} ({b.Rating}/5)"))}

Incomplete series (gaps to fill):
{(gapsText.Count > 0 ? string.Join("\n", gapsText) : "No incomplete series.")}";
    }
}
