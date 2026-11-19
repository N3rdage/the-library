using System.Globalization;
using System.Text;

namespace BookTracker.Application.Loans;

/// <summary>
/// Writes open loans as an RFC 5545 calendar: one all-day event per loan on its
/// due date, with a display alarm at 09:00 that day. The UID is derived from
/// the loan id, so adding the same loan again updates the event rather than
/// duplicating it.
/// </summary>
public static class LoanCalendar
{
    private const int MaxLineOctets = 75;

    public static string Write(IReadOnlyList<LoanCalendarEntry> entries, string host, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        var stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        AppendLine(sb, "BEGIN:VCALENDAR");
        AppendLine(sb, "VERSION:2.0");
        AppendLine(sb, "PRODID:-//BookTracker//Loans//EN");
        AppendLine(sb, "CALSCALE:GREGORIAN");
        AppendLine(sb, "METHOD:PUBLISH");

        foreach (var entry in entries)
        {
            var summary = Summary(entry);
            var description =
                $"\"{entry.Title}\" was lent to {entry.BorrowerName} through Bookcase " +
                $"and is due back on {entry.DueOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}.";

            AppendLine(sb, "BEGIN:VEVENT");
            AppendLine(sb, $"UID:loan-{entry.LoanId.ToString(CultureInfo.InvariantCulture)}@{Escape(host)}");
            AppendLine(sb, $"DTSTAMP:{stamp}");
            AppendLine(sb, $"DTSTART;VALUE=DATE:{Date(entry.DueOn)}");
            AppendLine(sb, $"DTEND;VALUE=DATE:{Date(entry.DueOn.AddDays(1))}");
            AppendLine(sb, $"SUMMARY:{Escape(summary)}");
            AppendLine(sb, $"DESCRIPTION:{Escape(description)}");
            AppendLine(sb, "BEGIN:VALARM");
            AppendLine(sb, "ACTION:DISPLAY");
            AppendLine(sb, $"DESCRIPTION:{Escape(summary)}");
            // An all-day event starts at local midnight on the due date, so a
            // trigger nine hours after the start fires at 09:00 local that day.
            AppendLine(sb, "TRIGGER:PT9H");
            AppendLine(sb, "END:VALARM");
            AppendLine(sb, "END:VEVENT");
        }

        AppendLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>The event title, also used as the alarm text.</summary>
    public static string Summary(LoanCalendarEntry entry) =>
        $"Return \"{entry.Title}\" to {entry.BorrowerName}";

    /// <summary>Escapes a TEXT value (RFC 5545 §3.3.11): backslash, semicolon,
    /// comma and line breaks.</summary>
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case ';': sb.Append(@"\;"); break;
                case ',': sb.Append(@"\,"); break;
                case '\r':
                    sb.Append(@"\n");
                    if (i + 1 < value.Length && value[i + 1] == '\n') i++;
                    break;
                case '\n': sb.Append(@"\n"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string Date(DateOnly date) =>
        date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    // Folds a content line at 75 octets of UTF-8 (RFC 5545 §3.1): each
    // continuation line starts with a space, which counts towards its 75, and
    // a multi-byte character is never split across lines.
    private static void AppendLine(StringBuilder sb, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                sb.Append("\r\n ");
                octets = 1;
            }
            sb.Append(rune.ToString());
            octets += size;
        }
        sb.Append("\r\n");
    }
}
