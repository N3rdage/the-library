using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Adds a Copy in Good condition to the Edition stored under an exact
/// ISBN, and attaches the named tags (find-or-create) to its Book, in one save.
/// Returns the new Copy's id, or null when no edition carries the ISBN (the
/// caller then creates the book).</summary>
public sealed record AddScannedCopy(string Isbn, IReadOnlyList<string> TagNames) : ICommand<int?>;

public sealed class AddScannedCopyHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddScannedCopy, int?>
{
    public async Task<int?> HandleAsync(AddScannedCopy command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var edition = await db.Editions
            .Include(e => e.Book).ThenInclude(b => b.Tags)
            .FirstOrDefaultAsync(e => e.Isbn == command.Isbn, ct);
        if (edition is null) return null;

        var copy = edition.AddCopy(BookCondition.Good, null, null);
        await TagResolver.AttachToBookAsync(db, edition.Book, command.TagNames, ct);

        await db.SaveChangesAsync(ct);
        return copy.Id;
    }
}
