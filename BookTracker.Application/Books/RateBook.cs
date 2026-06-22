using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Sets a Book's 0–5 star rating.</summary>
public sealed record RateBook(int BookId, int Rating) : ICommand;

public sealed class RateBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<RateBook>
{
    public async Task HandleAsync(RateBook command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");
        book.Rate(command.Rating);
        await db.SaveChangesAsync(ct);
    }
}
