using System.Globalization;
using System.Text.RegularExpressions;

namespace BookTracker.Application.Formatting;

/// <summary>
/// Splits a free-text series-order string into the integer sort key stored in
/// <c>Book.SeriesOrder</c> (used for ordering + gap detection) and the optional
/// display override stored in <c>Book.SeriesOrderDisplay</c>.
///
/// Clean integers ("4") need no display override — the int renders fine and the
/// display column stays null. Non-integer orders ("4.5" interquels like
/// <i>Edgedancer</i>, "1A" hierarchical positions) keep the raw string for
/// display and floor to the leading integer so the book still sorts next to its
/// neighbours instead of sinking to the bottom via the <c>int.MaxValue</c>
/// null-fallback.
///
/// This is the single source of truth for interpreting a free-text order token
/// (lookup-accept path and manual entry both route through it). The lookup-side
/// <c>BookLookupService.ParseOpenLibrarySeries</c> only splits a series
/// <i>string</i> into name + raw token + a clean-integer signal; it deliberately
/// does not floor, so the interquel rule isn't duplicated.
/// </summary>
public static partial class SeriesOrderParser
{
    /// <summary>
    /// Parses a user- or API-supplied order token into (sort int, display override).
    /// <list type="bullet">
    ///   <item><description><c>"4"</c> → (4, null) — clean int, no override needed</description></item>
    ///   <item><description><c>"4.5"</c> → (4, "4.5") — floor for sort, raw for display</description></item>
    ///   <item><description><c>"1A"</c> → (1, "1A")</description></item>
    ///   <item><description><c>"II"</c> / <c>""</c> / null → (null, raw-or-null)</description></item>
    /// </list>
    /// </summary>
    public static (int? Order, string? Display) Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var trimmed = raw.Trim();

        // Clean integer: no display override — the stored int renders fine.
        // Series order is 1-based, so reject 0 / negatives outright rather than
        // storing a position that sorts ahead of #1 and is invisible to gap
        // detection (Enumerable.Range(1, N)).
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clean))
            return clean >= 1 ? (clean, null) : (null, null);

        // Non-integer: preserve the raw label and floor to the leading integer
        // (if any, and only when positive) so the work sorts in the right
        // neighbourhood. A leading run that overflows int leaves the sort key
        // null — the label is still kept.
        var leading = LeadingIntegerRegex().Match(trimmed);
        int? order = leading.Success
            && int.TryParse(leading.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n >= 1
            ? n
            : null;
        return (order, trimmed);
    }

    /// <summary>
    /// Display label for a stored (order, display) pair — the override wins when
    /// present, otherwise the integer. Null when neither is set.
    /// </summary>
    public static string? Format(int? order, string? display)
        => !string.IsNullOrWhiteSpace(display)
            ? display
            : order?.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d+)")]
    private static partial Regex LeadingIntegerRegex();
}
