using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

/// <summary>Promotes an alias author to a canonical (standalone) author by
/// clearing its <c>CanonicalAuthorId</c>. No-op if the author is already
/// canonical. Lifted from AuthorDetailViewModel.PromoteToCanonicalAsync
/// (PR6b-2).</summary>
public sealed record PromoteAuthorToCanonical(int AuthorId) : ICommand<AuthorAdminResult>;

public sealed class PromoteAuthorToCanonicalHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<PromoteAuthorToCanonical, AuthorAdminResult>
{
    public async Task<AuthorAdminResult> HandleAsync(PromoteAuthorToCanonical command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alias = await db.Authors.FirstOrDefaultAsync(a => a.Id == command.AuthorId, ct);
        if (alias is null || alias.CanonicalAuthorId is null) return AuthorAdminResult.NoOp;

        alias.CanonicalAuthorId = null;
        await db.SaveChangesAsync(ct);
        return AuthorAdminResult.Ok($"\"{alias.Name}\" is now its own canonical author.");
    }
}
