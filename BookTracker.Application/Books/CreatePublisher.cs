using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>
/// Eagerly find-or-create a <c>Publisher</c> by name and return its id.
/// Dispatched by the publisher autocompletes the moment the user commits a
/// name (TD-15a) — picking a suggestion or leaving free text in the field —
/// so the row exists immediately rather than being created during the Edition
/// save. Idempotent: an existing name returns its id, a blank name is a no-op
/// returning null.
///
/// Plain find-or-create via <see cref="PublisherResolver"/> (so name handling
/// lives in one place). The check-then-insert race is the accepted single-user
/// residual tracked as TD-15 — moving creation here just localises it to a tiny
/// dedicated command instead of the contended aggregate save. Mirrors
/// <c>CreateAuthor</c>.
/// </summary>
public sealed record CreatePublisher(string? Name) : ICommand<int?>;

public sealed class CreatePublisherHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<CreatePublisher, int?>
{
    public async Task<int?> HandleAsync(CreatePublisher command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var publisher = await PublisherResolver.ResolveAsync(db, command.Name, ct);
        if (publisher is null) return null;
        await db.SaveChangesAsync(ct);
        return publisher.Id;
    }
}
