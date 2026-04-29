using System.Text.RegularExpressions;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public partial class SeriesMatchService(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    /// <summary>
    /// Returns a series suggestion using upstream series data from an ISBN
    /// lookup if present, otherwise falls back to the local title+author
    /// pattern matching. Prefer this overload over the title/author one in
    /// any flow that has a <see cref="BookLookupResult"/> in hand — Open
    /// Library's `series` field is far more reliable than name pattern
    /// guessing once the lookup actually returned series data.
    /// </summary>
    public async Task<SeriesMatch?> FindMatchAsync(BookLookupResult lookup)
    {
        if (!string.IsNullOrWhiteSpace(lookup.Series))
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var apiSeriesName = lookup.Series.Trim();

            // Local match by name (case-insensitive). When the API series
            // matches an existing local Series, attach to that one — the
            // user might have already created it from a previous capture.
            var localMatch = await db.Series
                .Where(s => s.Name.ToLower() == apiSeriesName.ToLower())
                .FirstOrDefaultAsync();

            var orderHint = FormatOrderHint(lookup.SeriesNumber, lookup.SeriesNumberRaw);

            if (localMatch is not null)
            {
                return new SeriesMatch(
                    localMatch.Id, localMatch.Name, localMatch.Type,
                    MatchReason.ApiMatchExisting,
                    $"{lookup.Source} indicates this is part of \"{localMatch.Name}\"{orderHint}");
            }

            // No local match — propose creating a new series. SeriesId is
            // null; SeriesName carries the proposed name so the UI / save
            // path can find-or-create on accept (PR-B follow-up will wire
            // this up; today the banner just informs the user).
            return new SeriesMatch(
                null, apiSeriesName, null,
                MatchReason.ApiMatchNewSeries,
                $"{lookup.Source} suggests this is part of \"{apiSeriesName}\"{orderHint} — not yet in the library; create it on the Series page after saving.");
        }

        // No upstream series data — fall back to local title/author matching.
        return await FindMatchAsync(lookup.Title, lookup.Author);
    }

    private static string FormatOrderHint(int? seriesNumber, string? seriesNumberRaw)
    {
        if (seriesNumber is int n) return $" #{n}";
        if (!string.IsNullOrWhiteSpace(seriesNumberRaw))
        {
            // Non-integer order from upstream (e.g. "5.5", "1A") — surface
            // the source value so the user can set Work.SeriesOrder manually
            // if they want a position. Cannot store directly; tracked as a
            // follow-up TODO ("Support non-integer / hierarchical SeriesOrder").
            return $" (order '{seriesNumberRaw}', left blank)";
        }
        return string.Empty;
    }

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
        var authorName = author.Trim();
        var authorWorkCount = await db.Works
            .CountAsync(w => w.Author.Name == authorName && w.SeriesId == null);

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
    TitlePattern,
    /// <summary>Upstream lookup (Open Library) named a series that already exists locally.</summary>
    ApiMatchExisting,
    /// <summary>Upstream lookup named a series not yet in the local library.</summary>
    ApiMatchNewSeries,
}
