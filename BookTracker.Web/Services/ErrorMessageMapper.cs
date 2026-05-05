using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

/// <summary>
/// Maps thrown exceptions surfaced on /Error to a user-readable
/// (Title, Body) pair. Stays terse on purpose: production errors get a
/// trace id alongside this so support can quote the technical detail
/// from App Insights — the message is the human-facing layer.
///
/// Categories cover the two repeat shapes BookTracker actually hits:
/// EF save failures (DbUpdateException — unique-index, FK, conversion)
/// and external-service flakes (HttpRequestException — Open Library /
/// Google Books / Trove / Anthropic timeouts). Everything else falls
/// through to the generic message; new categories get added when real
/// production logs surface a third shape worth distinguishing.
/// </summary>
public static class ErrorMessageMapper
{
    public record FriendlyMessage(string Title, string Body);

    public static FriendlyMessage Map(Exception? ex) => ex switch
    {
        DbUpdateException => new(
            "Couldn't save your change",
            "The database couldn't accept that update. The error has been logged. Try again, and if it keeps failing quote the trace ID below to support."),
        HttpRequestException => new(
            "Couldn't reach an external service",
            "BookTracker depends on a few external services for ISBN lookup and AI features — one of them didn't respond in time. The error has been logged."),
        _ => new(
            "Something went wrong",
            "An unexpected error occurred. The error has been logged. Quote the trace ID below to support and we'll take a look."),
    };
}
