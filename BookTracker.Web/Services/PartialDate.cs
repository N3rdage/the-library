using System.Globalization;
using System.Text.RegularExpressions;
using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

// Pair of (date, precision) used as the form-input shape for date fields
// where the user often only knows year or month-year. Storage stays as
// `DateOnly?` + `DatePrecision` columns on the entity; this record is
// the in-memory container that owns the round-trip from string text in
// a form to the two columns and back.
public record PartialDate(DateOnly? Date, DatePrecision Precision)
{
    public static PartialDate Empty => new(null, DatePrecision.Day);
}

// Parses free-form date text from form inputs. Accepts:
//   "1973"                  → Year
//   "1973-10"  / "10/1973"  → Month
//   "1973-10-12"            → Day
//   "Oct 1973" / "October 1973"  → Month
//   "12 Oct 1973" / "12 October 1973"  → Day
//
// Returns null when the input is non-empty but doesn't match any pattern,
// so the form can surface "couldn't read that date" without silently
// dropping the user's typing.
public static partial class PartialDateParser
{
    public static PartialDate? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return PartialDate.Empty;

        var trimmed = input.Trim();

        // Year only — 4 digits.
        if (YearOnly().IsMatch(trimmed))
        {
            var year = int.Parse(trimmed, CultureInfo.InvariantCulture);
            if (year is < 1400 or > 2999) return null;
            return new PartialDate(new DateOnly(year, 1, 1), DatePrecision.Year);
        }

        // ISO month YYYY-MM
        var isoMonth = IsoMonth().Match(trimmed);
        if (isoMonth.Success)
        {
            var year = int.Parse(isoMonth.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(isoMonth.Groups[2].Value, CultureInfo.InvariantCulture);
            if (year is < 1400 or > 2999 || month is < 1 or > 12) return null;
            return new PartialDate(new DateOnly(year, month, 1), DatePrecision.Month);
        }

        // ISO day YYYY-MM-DD
        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return new PartialDate(iso, DatePrecision.Day);
        }

        // M/YYYY (numeric month, four-digit year)
        var slashMonth = SlashMonth().Match(trimmed);
        if (slashMonth.Success)
        {
            var month = int.Parse(slashMonth.Groups[1].Value, CultureInfo.InvariantCulture);
            var year = int.Parse(slashMonth.Groups[2].Value, CultureInfo.InvariantCulture);
            if (year is < 1400 or > 2999 || month is < 1 or > 12) return null;
            return new PartialDate(new DateOnly(year, month, 1), DatePrecision.Month);
        }

        // "Oct 1973" or "October 1973"
        if (TryParseExact(trimmed, "MMM yyyy", out var monthShort)) return new(monthShort, DatePrecision.Month);
        if (TryParseExact(trimmed, "MMMM yyyy", out var monthLong)) return new(monthLong, DatePrecision.Month);

        // "12 Oct 1973" or "12 October 1973"
        if (TryParseExact(trimmed, "d MMM yyyy", out var dayShort)) return new(dayShort, DatePrecision.Day);
        if (TryParseExact(trimmed, "d MMMM yyyy", out var dayLong)) return new(dayLong, DatePrecision.Day);

        // Last-resort culture-invariant DateOnly.TryParse for anything else
        // (covers locale-agnostic formats like "1973-10-12T00:00:00").
        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
        {
            return new PartialDate(loose, DatePrecision.Day);
        }

        return null;
    }

    public static string Format(DateOnly? date, DatePrecision precision)
    {
        if (date is null) return "";
        var d = date.Value;
        return precision switch
        {
            DatePrecision.Year => d.Year.ToString(CultureInfo.InvariantCulture),
            DatePrecision.Month => d.ToString("MMM yyyy", CultureInfo.InvariantCulture),
            _ => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    private static bool TryParseExact(string input, string format, out DateOnly date) =>
        DateOnly.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex YearOnly();

    [GeneratedRegex(@"^(\d{4})-(\d{1,2})$")]
    private static partial Regex IsoMonth();

    [GeneratedRegex(@"^(\d{1,2})/(\d{4})$")]
    private static partial Regex SlashMonth();
}
