using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Persists a new default cover URL for a Book, the cover the library
/// list and the Book page header show. Used when a photo the user took becomes
/// the cover of a book being added.</summary>
public sealed record SetBookCover(int BookId, string Url) : ICommand;

public sealed class SetBookCoverHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<SetBookCover>
{
    public async Task HandleAsync(SetBookCover command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");

        book.SetCover(command.Url);
        await db.SaveChangesAsync(ct);
    }
}
