using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Marks a Book as read in one atomic gesture: sets status to Read
/// and records the rating + notes together. This is a task-based command
/// (convention C10) — it exists because "mark read" is a single user intention
/// that the List page's quick-action fires as one gesture; decomposing it into
/// SetBookStatus + RateBook + UpdateBookNotes would mean three loads, three
/// saves, three UpdatedAt bumps, and a non-atomic half-applied state on
/// failure.</summary>
public sealed record MarkBookRead(int BookId, int Rating, string? Notes);

public sealed class MarkBookReadHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task HandleAsync(MarkBookRead command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");
        book.MarkRead(command.Rating, command.Notes);
        await db.SaveChangesAsync(ct);
    }
}
