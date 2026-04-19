using System.Globalization;
using System.Text.RegularExpressions;
using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

// Normalises raw upstream metadata (Open Library's `physical_format` string,
// Google Books-style dimensions) into one of the four BookFormat values.
// Returns null when the inputs aren't confident enough to commit to a value
// — callers should preserve their existing default in that case rather than
// guessing.
public static class BookFormatNormalizer
{
    public static BookFormat? Normalize(string? physicalFormat, string? physicalDimensions)
    {
        var fromString = FromPhysicalFormatString(physicalFormat);
        if (fromString is not null) return fromString;

        return FromDimensions(physicalDimensions);
    }

    private static BookFormat? FromPhysicalFormatString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.ToLowerInvariant();

        // Order matters: the more specific labels must win over the generic
        // "paperback" / "hardcover" matches.
        if (s.Contains("large print") || s.Contains("large type")) return BookFormat.LargePrint;
        if (s.Contains("mass market")) return BookFormat.MassMarketPaperback;
        if (s.Contains("hardcover") || s.Contains("hardback") || s.Contains("hard cover") || s.Contains("hard back")) return BookFormat.Hardcover;
        if (s.Contains("paperback") || s.Contains("softcover") || s.Contains("soft cover") || s.Contains("trade pb")) return BookFormat.TradePaperback;

        return null;
    }

    // Open Library's physical_dimensions field looks like "7.5 x 5 x 0.6
    // inches" or "19.05 x 12.7 x 1.27 centimeters". Without a format string
    // we can only reliably distinguish mass-market (small) from trade-or-
    // larger — hardcover vs trade paperback isn't decidable from outer
    // dimensions alone.
    private static readonly Regex DimensionsRegex = new(
        @"^\s*(?<a>[\d.]+)\s*x\s*(?<b>[\d.]+)(?:\s*x\s*(?<c>[\d.]+))?\s*(?<unit>inches?|in|centimeters?|centimetres?|cm)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static BookFormat? FromDimensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var m = DimensionsRegex.Match(raw);
        if (!m.Success) return null;

        if (!double.TryParse(m.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) return null;
        if (!double.TryParse(m.Groups["b"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return null;

        var unit = m.Groups["unit"].Value;
        var inCm = unit.Length > 0 && unit.StartsWith("c", StringComparison.OrdinalIgnoreCase);
        if (!inCm)
        {
            // Default to inches when unit is missing (Open Library frequently
            // omits the unit; their data is overwhelmingly imperial).
            a *= 2.54;
            b *= 2.54;
        }

        // Mass-market paperback is roughly 17.8 x 10.5 cm. Allow a small
        // tolerance — anything noticeably larger on either axis is a trade
        // (or hardcover, which we can't tell apart from dimensions).
        var height = Math.Max(a, b);
        var width = Math.Min(a, b);
        if (height <= 19.0 && width <= 11.5)
        {
            return BookFormat.MassMarketPaperback;
        }

        return null;
    }
}
