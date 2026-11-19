using System.Text;
using BookTracker.Application.Loans;
using Xunit;

namespace BookTracker.Tests;

// Unit tests for the pure .ics writer behind the "Add to calendar" downloads.
[Trait("Category", TestCategories.Unit)]
public class LoanCalendarTests
{
    private const string Host = "books.example.test";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 30, 5, TimeSpan.FromHours(11));

    private static LoanCalendarEntry Entry(int id = 7, string title = "Dune", string borrower = "Sam", DateOnly? dueOn = null) =>
        new(id, title, borrower, dueOn ?? new DateOnly(2026, 12, 3));

    private static string Write(params LoanCalendarEntry[] entries) => LoanCalendar.Write(entries, Host, Now);

    // Content lines with folding undone (RFC 5545 §3.1).
    private static string[] Unfolded(string ics) =>
        ics.Replace("\r\n ", "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void EmptyList_isAValidCalendarWithNoEvents()
    {
        var ics = Write();

        Assert.Equal(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//BookTracker//Loans//EN\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\nEND:VCALENDAR\r\n",
            ics);
    }

    [Fact]
    public void OneEntry_writesTheEventAndItsAlarm()
    {
        var lines = Unfolded(Write(Entry()));

        Assert.Equal(
        [
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//BookTracker//Loans//EN",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            "UID:loan-7@books.example.test",
            "DTSTAMP:20261119T013005Z",
            "DTSTART;VALUE=DATE:20261203",
            "DTEND;VALUE=DATE:20261204",
            "SUMMARY:Return \"Dune\" to Sam",
            "DESCRIPTION:\"Dune\" was lent to Sam through Bookcase and is due back on 3 December 2026.",
            "BEGIN:VALARM",
            "ACTION:DISPLAY",
            "DESCRIPTION:Return \"Dune\" to Sam",
            "TRIGGER:PT9H",
            "END:VALARM",
            "END:VEVENT",
            "END:VCALENDAR",
        ], lines);
    }

    [Fact]
    public void EveryLine_endsInCrlf_andThereAreNoBareLineFeeds()
    {
        var ics = Write(Entry(title: "Line one\nline two"), Entry(id: 8));

        Assert.EndsWith("\r\n", ics);
        Assert.Equal(ics.Count(c => c == '\n'), ics.Split("\r\n").Length - 1);
        Assert.DoesNotContain('\r', ics.Replace("\r\n", ""));
    }

    [Fact]
    public void OneEventPerEntry()
    {
        var lines = Unfolded(Write(Entry(1), Entry(2), Entry(3)));

        Assert.Equal(3, lines.Count(l => l == "BEGIN:VEVENT"));
        Assert.Equal(3, lines.Count(l => l == "END:VEVENT"));
        Assert.Equal(3, lines.Count(l => l == "BEGIN:VALARM"));
        Assert.Equal(
            ["UID:loan-1@books.example.test", "UID:loan-2@books.example.test", "UID:loan-3@books.example.test"],
            lines.Where(l => l.StartsWith("UID:")));
    }

    [Fact]
    public void Uid_dependsOnlyOnTheLoanAndTheHost()
    {
        var first = LoanCalendar.Write([Entry(id: 42, title: "Dune", dueOn: new DateOnly(2026, 12, 1))], Host, Now);
        var later = LoanCalendar.Write([Entry(id: 42, title: "Dune Messiah", dueOn: new DateOnly(2027, 1, 9))], Host, Now.AddDays(5));

        var uid = Assert.Single(Unfolded(first), l => l.StartsWith("UID:"));
        Assert.Equal("UID:loan-42@books.example.test", uid);
        Assert.Equal(uid, Assert.Single(Unfolded(later), l => l.StartsWith("UID:")));
    }

    [Theory]
    [InlineData("a\\b", @"a\\b")]
    [InlineData("a;b", @"a\;b")]
    [InlineData("a,b", @"a\,b")]
    [InlineData("a\nb", @"a\nb")]
    [InlineData("a\r\nb", @"a\nb")]
    [InlineData("a\rb", @"a\nb")]
    [InlineData("plain \"quoted\" text: ok", "plain \"quoted\" text: ok")]
    public void Escape_handlesEachSpecialCharacter(string input, string expected)
    {
        Assert.Equal(expected, LoanCalendar.Escape(input));
    }

    [Fact]
    public void SpecialCharactersInTitleAndBorrower_areEscapedInTheEvent()
    {
        var lines = Unfolded(Write(Entry(title: "Eats, Shoots; Leaves\\", borrower: "Jo\nSmith")));

        Assert.Contains("SUMMARY:Return \"Eats\\, Shoots\\; Leaves\\\\\" to Jo\\nSmith", lines);
    }

    [Fact]
    public void LongNonAsciiTitle_isFoldedAt75Octets_withoutSplittingACharacter()
    {
        var title = string.Concat(Enumerable.Repeat("Les Misérables — 日本語の本 ", 6));
        var ics = Write(Entry(title: title));

        var physical = ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.All(physical, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"{Encoding.UTF8.GetByteCount(line)} octets: {line}"));
        Assert.Contains(physical, line => line.StartsWith(' '));
        // A long line is filled up to the limit rather than broken early: the
        // first physical SUMMARY line is within three octets (one character) of 75.
        var summaryStart = Assert.Single(physical, l => l.StartsWith("SUMMARY:"));
        Assert.InRange(Encoding.UTF8.GetByteCount(summaryStart), 72, 75);
        // No replacement characters: every multi-byte character survived intact.
        Assert.DoesNotContain('�', ics);
        Assert.Contains($"SUMMARY:Return \"{title}\" to Sam", Unfolded(ics));
    }

    [Fact]
    public void LineOfExactly75Octets_isNotFolded()
    {
        // "SUMMARY:Return \"" + title + "\" to Sam" is 24 octets plus the title.
        var title = new string('x', 75 - 24);
        var physical = Write(Entry(title: title)).Split("\r\n");

        Assert.Contains($"SUMMARY:Return \"{title}\" to Sam", physical);
    }

    [Theory]
    [InlineData(2026, 11, 30, "20261130", "20261201")]
    [InlineData(2026, 12, 31, "20261231", "20270101")]
    [InlineData(2028, 2, 28, "20280228", "20280229")]
    [InlineData(2027, 2, 28, "20270228", "20270301")]
    public void AllDayEvent_endsTheNextDay_acrossMonthAndYearEnds(int y, int m, int d, string start, string end)
    {
        var lines = Unfolded(Write(Entry(dueOn: new DateOnly(y, m, d))));

        Assert.Contains($"DTSTART;VALUE=DATE:{start}", lines);
        Assert.Contains($"DTEND;VALUE=DATE:{end}", lines);
    }
}
