using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

/// <summary>
/// Eagerly find-or-create an <c>Author</c> by name and return its id.
/// Dispatched by the author/contributor pickers the moment the user commits a
/// name (TD-15a), so the row exists immediately rather than being created
/// during the aggregate save. Idempotent — an existing name returns its id, so
/// committing a name that already exists is a harmless no-op.
///
/// Plain find-or-create via <see cref="AuthorResolver"/> (so name handling lives
/// in one place). The check-then-insert race is the accepted single-user
/// residual tracked as TD-15 — moving creation here just localises it to a tiny
/// dedicated command instead of the contended aggregate save.
/// </summary>
public sealed record CreateAuthor(string Name) : ICommand<int>;

public sealed class CreateAuthorHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<CreateAuthor, int>
{
    public async Task<int> HandleAsync(CreateAuthor command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var author = await AuthorResolver.FindOrCreateAsync(command.Name, db, ct);
        await db.SaveChangesAsync(ct);
        return author.Id;
    }
}
