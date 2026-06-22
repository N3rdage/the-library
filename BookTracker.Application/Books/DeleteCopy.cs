using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Deletes a Copy from a Book. If it was the Edition's last Copy the
/// Edition is removed too (the last-copy invariant lives on the aggregate).
/// Loads the Book root because the rule spans Edition → Copies.</summary>
public sealed record DeleteCopy(int BookId, int CopyId) : ICommand;

public sealed class DeleteCopyHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<DeleteCopy>
{
    public async Task HandleAsync(DeleteCopy command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .Include(b => b.Editions)
                .ThenInclude(e => e.Copies)
            .FirstOrDefaultAsync(b => b.Id == command.BookId, ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");

        book.RemoveCopy(command.CopyId);
        await db.SaveChangesAsync(ct);
    }
}
