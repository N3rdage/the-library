using BookTracker.Application;
using BookTracker.Application.Works;
using BookTracker.Shared.Online;
using BookTracker.Web.Services;

namespace BookTracker.Web.Api;

// The online layer for Bookshelf: answers the phone when its offline cache
// misses. The phone calls Bookcase rather than the providers so the provider
// keys stay server-side and the owned-work match can read the whole library.
// Auth: the same Easy Auth platform-layer gate as every other /api route
// (SECURITY-AUDIT.md, SEC-006).
public static class OnlineEndpoints
{
    public static IEndpointRouteBuilder MapOnlineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/online/isbn/{isbn}", HandleAsync);
        app.MapGet("/api/online/author-works", HandleAuthorWorksAsync);
        return app;
    }

    // Internal so the unit tests drive it without a host. The ISBN is
    // cleaned here as well as in BookLookupService because a bad one must be
    // a 400 rather than the 404 the service's null would become, and because
    // the owned-edition match needs the stored (cleaned) form.
    internal static async Task<IResult> HandleAsync(
        string isbn,
        IBookLookupService lookup,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        var clean = new string(isbn.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (clean.Length is not (10 or 13))
            return Results.BadRequest("An ISBN has 10 or 13 characters once spaces and hyphens are removed.");

        var found = await lookup.LookupByIsbnAsync(clean, ct);
        if (found is null) return Results.NotFound();

        var owned = await dispatcher.Query(new FindOwnedWork(clean, found.Title, found.Author), ct);

        return Results.Ok(new OnlineIsbnLookup(
            found.Isbn,
            found.Title,
            found.Subtitle,
            found.Author,
            found.Publisher,
            found.CoverUrl,
            found.DatePrinted?.Year,
            found.Source,
            owned));
    }

    // Internal for the same reason as HandleAsync. The title match is the
    // Application query's (its normaliser is internal there); this only
    // copies the BookIds it returns onto Open Library's rows.
    internal static async Task<IResult> HandleAuthorWorksAsync(
        string? author,
        IBookLookupService lookup,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(author))
            return Results.BadRequest("An author name is required.");

        var name = author.Trim();
        var works = await lookup.GetAuthorWorksAsync(name, ct);
        if (works.Count == 0) return Results.Ok(new OnlineAuthorWorks(name, []));

        var titles = works
            .Select(w => w.Title)
            .OfType<string>()
            .Distinct()
            .ToList();
        var owned = await dispatcher.Query(new GetOwnedWorksByAuthor(name, titles), ct);

        return Results.Ok(new OnlineAuthorWorks(
            name,
            works
                .Select(w => w with
                {
                    OwnedBookId = w.Title is not null && owned.TryGetValue(w.Title, out var bookId) ? bookId : null,
                })
                .ToList()));
    }
}
