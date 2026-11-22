using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Works;
using BookTracker.Shared.Online;
using BookTracker.Web.Services;

namespace BookTracker.Web.Api;

// The online layer for Bookshelf: answers the phone when its offline cache
// misses. The phone calls Bookcase rather than the providers so the provider
// keys stay server-side and the owned-work match can read the whole library.
// Auth: the same Easy Auth platform-layer gate as every other /api route
// (configured in infra/modules/app-config.bicep).
public static class OnlineEndpoints
{
    // Each call fans out to the providers, so one window is shared by all
    // three routes (and all callers: Bookcase has a single owner). It guards
    // the provider quotas against a client loop, not against an attacker.
    public const string RateLimitPolicy = "online-lookups";
    public const int PermitLimit = 60;
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    public static IEndpointRouteBuilder MapOnlineEndpoints(this IEndpointRouteBuilder app)
    {
        var online = app.MapGroup("/api/online").RequireRateLimiting(RateLimitPolicy);
        online.MapGet("/isbn/{isbn}", HandleAsync);
        online.MapGet("/author-works", HandleAuthorWorksAsync);
        online.MapGet("/work-editions", HandleWorkEditionsAsync);
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
            owned,
            found.WorkKey));
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

    // Internal for the same reason as HandleAsync. Ownership is by exact
    // ISBN: the service has already cleaned Open Library's ISBNs to the
    // stored form.
    internal static async Task<IResult> HandleWorkEditionsAsync(
        string? workKey,
        IBookLookupService lookup,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        if (!BookLookupService.IsOpenLibraryWorkKey(workKey))
            return Results.BadRequest("A work key has the form /works/OL…W, e.g. /works/OL45804W.");

        var editions = await lookup.GetWorkEditionsAsync(workKey!, ct);

        var isbns = editions
            .Select(e => e.Isbn)
            .OfType<string>()
            .Distinct()
            .ToList();
        if (isbns.Count == 0) return Results.Ok(new OnlineWorkEditions(workKey!, editions));

        var owned = await dispatcher.Query(new FindOwnedEditions(isbns), ct);

        return Results.Ok(new OnlineWorkEditions(
            workKey!,
            editions
                .Select(e => e with
                {
                    OwnedBookId = e.Isbn is not null && owned.TryGetValue(e.Isbn, out var bookId) ? bookId : null,
                })
                .ToList()));
    }
}
