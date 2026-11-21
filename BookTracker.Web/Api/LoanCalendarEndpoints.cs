using System.Text;
using BookTracker.Application;
using BookTracker.Application.Loans;

namespace BookTracker.Web.Api;

// "Add to calendar" for loans: an .ics download with an all-day event on each
// due date, written by LoanCalendar. Nothing is stored, sent or scheduled; the
// phone or desktop calendar that opens the file owns the reminder. Auth: the
// same Easy Auth platform-layer gate as every other /api route
// (configured in infra/modules/app-config.bicep).
public static class LoanCalendarEndpoints
{
    /// <summary>Host used in event UIDs when the request carries none.</summary>
    public const string FallbackHost = "books.silly.ninja";

    private const string CalendarContentType = "text/calendar; charset=utf-8";

    public static IEndpointRouteBuilder MapLoanCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/loans/{loanId:int}/calendar.ics", HandleLoanAsync);
        app.MapGet("/api/loans/calendar.ics", HandleBorrowerAsync);
        return app;
    }

    // Internal so the unit tests drive it without a host.
    internal static async Task<IResult> HandleLoanAsync(
        int loanId,
        HttpContext http,
        IDispatcher dispatcher,
        TimeProvider clock,
        CancellationToken ct)
    {
        var entries = await dispatcher.Query(new GetLoanCalendar(loanId, null), ct);
        return Calendar(http, entries, clock, $"loan-{loanId}.ics");
    }

    // Internal for the same reason as HandleLoanAsync. A blank name is a 404
    // rather than every borrower's loans.
    internal static async Task<IResult> HandleBorrowerAsync(
        string? borrower,
        HttpContext http,
        IDispatcher dispatcher,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(borrower)) return Results.NotFound();

        var name = borrower.Trim();
        var entries = await dispatcher.Query(new GetLoanCalendar(null, name), ct);
        return Calendar(http, entries, clock, $"loans-{Slug(name)}.ics");
    }

    private static IResult Calendar(
        HttpContext http, IReadOnlyList<LoanCalendarEntry> entries, TimeProvider clock, string fileName)
    {
        if (entries.Count == 0) return Results.NotFound();

        var host = http.Request.Host.HasValue ? http.Request.Host.Host : FallbackHost;
        var ics = LoanCalendar.Write(entries, host, clock.GetUtcNow());
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        return Results.Text(ics, CalendarContentType);
    }

    /// <summary>An ASCII-only file-name fragment: lower-case letters and digits,
    /// every other run of characters one hyphen; "borrower" when nothing is
    /// left.</summary>
    internal static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().TrimEnd('-');
        return slug.Length == 0 ? "borrower" : slug;
    }
}
