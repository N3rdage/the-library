using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Soft-deletes a Book. The Book row survives as a tombstone
/// (DeletedAt stamped, hidden by the global query filter) so the catalog
/// snapshot can emit it in deletedIds[]; its Editions (and Copies via cascade)
/// are hard-removed, and the Work/Tag join rows are cleared.</summary>
public sealed record DeleteBook(int BookId);

public sealed class DeleteBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task HandleAsync(DeleteBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .Include(b => b.Editions)
            .Include(b => b.Tags)
            .Include(b => b.Works)
            .FirstOrDefaultAsync(b => b.Id == command.BookId, ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");

        // SoftDelete() owns the whole operation — orphan-removes the Editions
        // (Copies cascade), clears the Work/Tag joins, stamps the tombstone.
        // The Includes above load the children so EF tracks the removals.
        book.SoftDelete();

        await db.SaveChangesAsync(ct);
    }
}
