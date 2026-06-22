using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Changes a Book's reading status.</summary>
public sealed record SetBookStatus(int BookId, BookStatus Status);

public sealed class SetBookStatusHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task HandleAsync(SetBookStatus command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");
        book.ChangeStatus(command.Status);
        await db.SaveChangesAsync(ct);
    }
}
