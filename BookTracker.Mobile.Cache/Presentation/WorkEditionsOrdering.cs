using System.Globalization;
using System.Text.RegularExpressions;
using BookTracker.Shared.Online;

namespace BookTracker.Mobile.Cache.Presentation;

/// <summary>One row of the Result page's "Other editions of this work" list.
/// <see cref="IsOwned"/> is true when Bookcase reported the edition as owned
/// or its ISBN is one the phone's cache holds.</summary>
public sealed record WorkEditionRow(
    string? Isbn,
    string? Title,
    string? Publisher,
    string? PublishDate,
    string? Format,
    string? CoverUrl,
    bool IsOwned);

/// <summary>Orders and de-duplicates the editions Bookcase's
/// GET /api/online/work-editions returns.</summary>
public static partial class WorkEditionsOrdering
{
    /// <summary>The row's title.</summary>
    public const string RowTitle = "Other editions of this work";

    /// <summary>The caption under the row while it is disabled (offline, no
    /// work key, or the fetch failed).</summary>
    public const string OnlineOnlyCaption = "online only";

    /// <summary>One row per ISBN (the first occurrence's details, owned when
    /// any occurrence is); editions without an ISBN are each kept. Owned rows
    /// first, then newest publish date first, undated last; ties keep the
    /// endpoint's order. An edition is owned when Bookcase gave it an
    /// <c>OwnedBookId</c> or its ISBN is in <paramref name="ownedIsbns"/>.</summary>
    public static IReadOnlyList<WorkEditionRow> Arrange(
        IReadOnlyList<OnlineEdition> editions,
        IEnumerable<string?> ownedIsbns)
    {
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var isbn in ownedIsbns)
        {
            var clean = CleanIsbn(isbn);
            if (clean is not null) owned.Add(clean);
        }

        var rows = new List<WorkEditionRow>(editions.Count);
        var indexByIsbn = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var edition in editions)
        {
            var clean = CleanIsbn(edition.Isbn);
            var isOwned = edition.OwnedBookId is not null || (clean is not null && owned.Contains(clean));

            if (clean is not null && indexByIsbn.TryGetValue(clean, out var at))
            {
                if (isOwned && !rows[at].IsOwned) rows[at] = rows[at] with { IsOwned = true };
                continue;
            }

            if (clean is not null) indexByIsbn[clean] = rows.Count;
            rows.Add(new WorkEditionRow(
                clean, edition.Title, edition.Publisher, edition.PublishDate, edition.Format, edition.CoverUrl, isOwned));
        }

        return rows
            .OrderByDescending(r => r.IsOwned)
            .ThenByDescending(r => ParseDate(r.PublishDate) is not null)
            .ThenByDescending(r => ParseDate(r.PublishDate))
            .ToList();
    }

    /// <summary>"Penguin  ·  Paperback  ·  1995": the parts Open Library gave,
    /// in that order.</summary>
    public static string Meta(WorkEditionRow row) =>
        string.Join("  ·  ", new[] { row.Publisher, row.Format, row.PublishDate }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()));

    /// <summary>Open Library's free-text publish date as a sortable date: a
    /// full date when it parses, else the first four-digit year in it (as
    /// 1 January), else null.</summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;

        var match = YearPattern().Match(text);
        return match.Success ? new DateTime(int.Parse(match.Value, CultureInfo.InvariantCulture), 1, 1) : null;
    }

    private static string? CleanIsbn(string? isbn)
    {
        if (string.IsNullOrWhiteSpace(isbn)) return null;
        var clean = new string(isbn.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return clean.Length == 0 ? null : clean;
    }

    [GeneratedRegex(@"(?<!\d)(1[0-9]{3}|20[0-9]{2})(?!\d)")]
    private static partial Regex YearPattern();
}
