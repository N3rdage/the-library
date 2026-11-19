using BookTracker.Shared.Online;

namespace BookTracker.Mobile.Cache.Presentation;

// Which answer Bookshelf's Result page gives once the cache has missed an ISBN
// and Bookcase's GET /api/online/isbn/{isbn} has (or has not) answered.
public enum OnlineResultState
{
    /// <summary>Offline, or the endpoint gave nothing: the page keeps its
    /// cache "not found" answer.</summary>
    Unknown,
    /// <summary>A provider knows the ISBN and the library owns neither the
    /// edition nor the work.</summary>
    NotOwned,
    /// <summary>The library owns the work in another printing.</summary>
    OwnedOtherEdition,
    /// <summary>The library owns this exact edition; the phone's cache is
    /// behind.</summary>
    OwnedSameEdition,
}

/// <summary>The copy for one online answer. Empty strings are lines the page
/// hides. <see cref="OwnedBookId"/> is the owned Book whose editions the page
/// lists from the cache (owned states only).</summary>
public sealed record OnlineResultAnswer(
    OnlineResultState State,
    string Headline,
    string Title,
    string Byline,
    string Details,
    string Note,
    int? OwnedBookId);

public static class OnlineResultFormatter
{
    // The page's existing cache-miss headline, kept so Unknown reads as today.
    public const string NotFoundHeadline = "Not in your library.";

    public static OnlineResultAnswer Decide(OnlineIsbnLookup? lookup, bool wasOnline)
    {
        if (!wasOnline || lookup is null)
            return new(OnlineResultState.Unknown, NotFoundHeadline, "", "", "", "", null);

        var byline = lookup.Author?.Trim() ?? "";
        var details = FormatDetails(lookup.Publisher, lookup.PublishedYear);

        if (lookup.Owned is { } owned)
        {
            // The provider's title names the scanned printing; the owned Book's
            // title stands in when the provider has none.
            var title = FormatTitle(lookup.Title, lookup.Subtitle);
            if (title.Length == 0) title = owned.BookTitle.Trim();

            return owned.SameEdition
                ? new(OnlineResultState.OwnedSameEdition, "In your library", title, byline, details,
                    "Your phone's copy of the library is behind. Sync to bring it up to date.", owned.BookId)
                : new(OnlineResultState.OwnedOtherEdition, "You have this work, not this edition", title, byline, details,
                    "", owned.BookId);
        }

        return new(OnlineResultState.NotOwned, "Not in your library", FormatTitle(lookup.Title, lookup.Subtitle),
            byline, details, "", null);
    }

    // "Title: Subtitle", either part optional.
    private static string FormatTitle(string? title, string? subtitle)
    {
        var t = title?.Trim() ?? "";
        var s = subtitle?.Trim() ?? "";
        if (t.Length == 0) return s;
        return s.Length == 0 ? t : $"{t}: {s}";
    }

    // "Gollancz  ·  1951" with the same separator as the book meta line.
    private static string FormatDetails(string? publisher, int? year)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(publisher)) parts.Add(publisher.Trim());
        if (year is int y) parts.Add(y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return string.Join("  ·  ", parts);
    }
}
