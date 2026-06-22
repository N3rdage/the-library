using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Updates the Book-level fields from the "edit details" dialog
/// (title, category, default cover URL).</summary>
public sealed record UpdateBookDetails(int BookId, string Title, BookCategory Category, string? CoverUrl) : ICommand;

public sealed class UpdateBookDetailsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<UpdateBookDetails>
{
    public async Task HandleAsync(UpdateBookDetails command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");
        book.UpdateDetails(command.Title, command.Category, command.CoverUrl);
        await db.SaveChangesAsync(ct);
    }
}
