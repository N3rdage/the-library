using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Updates a Book's free-text notes (trimmed; blank becomes null).</summary>
public sealed record UpdateBookNotes(int BookId, string? Notes);

public sealed class UpdateBookNotesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task HandleAsync(UpdateBookNotes command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");
        book.UpdateNotes(command.Notes);
        await db.SaveChangesAsync(ct);
    }
}
