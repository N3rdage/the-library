using System.Globalization;
using System.Text.RegularExpressions;

namespace BookTracker.Web.Services;

/// <summary>
/// Splits a free-text series-order string into the integer sort key stored in
/// <c>Work.SeriesOrder</c> (used for ordering + gap detection) and the optional
/// display override stored in <c>Work.SeriesOrderDisplay</c>.
///
/// Clean integers ("4") need no display override — the int renders fine and the
/// display column stays null. Non-integer orders ("4.5" interquels like
/// <i>Edgedancer</i>, "1A" hierarchical positions) keep the raw string for
/// display and floor to the leading integer so the work still sorts next to its
/// neighbours instead of sinking to the bottom via the <c>int.MaxValue</c>
/// null-fallback.
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
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clean))
            return (clean, null);

        // Non-integer: preserve the raw label and floor to the leading integer
        // (if any) so the work sorts in the right neighbourhood.
        var leading = LeadingIntegerRegex().Match(trimmed);
        int? order = leading.Success
            && int.TryParse(leading.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
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
