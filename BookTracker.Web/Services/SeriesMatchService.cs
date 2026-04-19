using System.Text.RegularExpressions;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

// TODO: enhance series detection with Open Library series data from ISBN
// lookup results. Currently uses local-only matching (author + title patterns).

public partial class SeriesMatchService(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    /// <summary>
    /// Checks if a book (by title and author) likely belongs to an existing series.
    /// Returns a match suggestion or null if no match found.
    /// </summary>
    public async Task<SeriesMatch?> FindMatchAsync(string? title, string? author)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync();

        // Strategy 1: Check if the author already has works in a series
        var authorSeries = await db.Series
            .Include(s => s.Works)
            .Where(s => s.Author != null && s.Author == author.Trim())
            .ToListAsync();

        if (authorSeries.Count > 0)
        {
            // If author has exactly one series, suggest it
            if (authorSeries.Count == 1)
            {
                var series = authorSeries[0];
                return new SeriesMatch(series.Id, series.Name, series.Type, MatchReason.AuthorMatch,
                    $"This author has an existing {series.Type.ToString().ToLowerInvariant()}: \"{series.Name}\"");
            }

            // Multiple series — check if title hints at one of them
            foreach (var series in authorSeries)
            {
                if (TitleContainsSeriesName(title, series.Name))
                {
                    return new SeriesMatch(series.Id, series.Name, series.Type, MatchReason.TitleAndAuthorMatch,
                        $"Title and author match the {series.Type.ToString().ToLowerInvariant()} \"{series.Name}\"");
                }
            }

            // Multiple series, no title match — suggest the first as a hint
            return new SeriesMatch(authorSeries[0].Id, authorSeries[0].Name, authorSeries[0].Type, MatchReason.AuthorMatch,
                $"This author has {authorSeries.Count} series/collections in the library");
        }

        // Strategy 2: Check if the author has other works (not yet in a series)
        // that might suggest grouping
        var authorWorkCount = await db.Works
            .CountAsync(w => w.Author == author.Trim() && w.SeriesId == null);

        if (authorWorkCount >= 2)
        {
            return new SeriesMatch(null, null, null, MatchReason.AuthorHasMultipleBooks,
                $"This author has {authorWorkCount} other works not in any series — consider creating a collection");
        }

        // Strategy 3: Title pattern matching for series indicators
        var orderNumber = ExtractSeriesNumber(title);
        if (orderNumber.HasValue)
        {
            return new SeriesMatch(null, null, null, MatchReason.TitlePattern,
                $"Title suggests this is book #{orderNumber} in a series");
        }

        return null;
    }

    private static bool TitleContainsSeriesName(string title, string seriesName)
    {
        var normalizedTitle = title.ToLowerInvariant();
        var normalizedSeries = seriesName.ToLowerInvariant();

        // Direct containment
        if (normalizedTitle.Contains(normalizedSeries) || normalizedSeries.Contains(normalizedTitle))
            return true;

        // Check if significant words from the series name appear in the title
        var seriesWords = normalizedSeries.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToList();

        if (seriesWords.Count > 0)
        {
            var matchCount = seriesWords.Count(w => normalizedTitle.Contains(w));
            return matchCount >= Math.Ceiling(seriesWords.Count * 0.6);
        }

        return false;
    }

    /// <summary>
    /// Extracts a series/volume number from a title.
    /// Matches patterns like "Book 3", "#2", "Vol. 1", "Volume 5", "Part II".
    /// </summary>
    internal static int? ExtractSeriesNumber(string title)
    {
        // "Book 3", "#2", "No. 5"
        var match = NumberPatternRegex().Match(title);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
            return num;

        // "Vol. 1", "Volume 3"
        match = VolumePatternRegex().Match(title);
        if (match.Success && int.TryParse(match.Groups[1].Value, out num))
            return num;

        // "Part II", "Part III" etc.
        match = RomanPartPatternRegex().Match(title);
        if (match.Success)
            return RomanToInt(match.Groups[1].Value);

        return null;
    }

    private static int? RomanToInt(string roman) => roman.Trim().ToUpperInvariant() switch
    {
        "I" => 1, "II" => 2, "III" => 3, "IV" => 4, "V" => 5,
        "VI" => 6, "VII" => 7, "VIII" => 8, "IX" => 9, "X" => 10,
        _ => null
    };

    [GeneratedRegex(@"(?:book|#|no\.?)\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberPatternRegex();

    [GeneratedRegex(@"vol(?:ume)?\.?\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VolumePatternRegex();

    [GeneratedRegex(@"part\s+(I{1,3}|IV|VI{0,3}|IX|X)", RegexOptions.IgnoreCase)]
    private static partial Regex RomanPartPatternRegex();
}

public record SeriesMatch(
    int? SeriesId,
    string? SeriesName,
    SeriesType? SeriesType,
    MatchReason Reason,
    string Message);

public enum MatchReason
{
    AuthorMatch,
    TitleAndAuthorMatch,
    AuthorHasMultipleBooks,
    TitlePattern
}
