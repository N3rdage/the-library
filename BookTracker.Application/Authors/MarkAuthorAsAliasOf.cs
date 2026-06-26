using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

/// <summary>Marks an author as a pen-name alias of a canonical author. Re-roots
/// if the chosen target is itself an alias (avoids alias-of-alias chains) and
/// re-points any authors that were aliases of this row at the new root, so an
/// existing alias graph collapses correctly. Lifted from
/// AuthorDetailViewModel.MarkAsAliasOfAsync (PR6b-2).</summary>
public sealed record MarkAuthorAsAliasOf(int AliasId, int CanonicalId) : ICommand<AuthorAdminResult>;

public sealed class MarkAuthorAsAliasOfHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<MarkAuthorAsAliasOf, AuthorAdminResult>
{
    public async Task<AuthorAdminResult> HandleAsync(MarkAuthorAsAliasOf command, CancellationToken ct = default)
    {
        if (command.AliasId == command.CanonicalId) return AuthorAdminResult.NoOp;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var alias = await db.Authors.FirstOrDefaultAsync(a => a.Id == command.AliasId, ct);
        var canonical = await db.Authors.FirstOrDefaultAsync(a => a.Id == command.CanonicalId, ct);
        if (alias is null || canonical is null) return AuthorAdminResult.NoOp;

        // Re-root if the chosen "canonical" is itself an alias — avoids alias-of-alias chains.
        var rootCanonicalId = canonical.CanonicalAuthorId ?? canonical.Id;

        alias.CanonicalAuthorId = rootCanonicalId;

        // Re-point any prior aliases that targeted this row at the new root.
        var prior = await db.Authors.Where(a => a.CanonicalAuthorId == command.AliasId).ToListAsync(ct);
        foreach (var p in prior)
        {
            p.CanonicalAuthorId = rootCanonicalId;
        }

        await db.SaveChangesAsync(ct);
        return AuthorAdminResult.Ok($"\"{alias.Name}\" is now an alias of \"{canonical.Name}\".");
    }
}
