using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

/// <summary>Renames an author. Refuses (with a user-facing message) if another
/// author already has the trimmed name — <c>Author.Name</c> is uniquely indexed,
/// so the user is steered to the alias dropdown to merge instead. Lifted from
/// AuthorDetailViewModel.RenameAsync (PR6b-2).</summary>
public sealed record RenameAuthor(int AuthorId, string NewName) : ICommand<AuthorAdminResult>;

public sealed class RenameAuthorHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RenameAuthor, AuthorAdminResult>
{
    public async Task<AuthorAdminResult> HandleAsync(RenameAuthor command, CancellationToken ct = default)
    {
        var trimmed = command.NewName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return AuthorAdminResult.NoOp;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var author = await db.Authors.FirstOrDefaultAsync(a => a.Id == command.AuthorId, ct);
        if (author is null) return AuthorAdminResult.NoOp;

        var clash = await db.Authors.AnyAsync(a => a.Id != command.AuthorId && a.Name == trimmed, ct);
        if (clash)
        {
            return AuthorAdminResult.Error(
                $"An author named \"{trimmed}\" already exists. Use the alias dropdown to merge.");
        }

        author.Name = trimmed;
        await db.SaveChangesAsync(ct);
        return AuthorAdminResult.Ok($"Renamed to \"{trimmed}\".");
    }
}
