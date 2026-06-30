using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Updates the Book-level fields from the "edit details" dialog
/// (title, category, default cover URL, and series membership + order). The
/// series-order label arrives already parsed (the VM owns the free-text
/// parsing); membership targets an existing Series by id. The series args are
/// required (not defaulted) because a save always reconciles membership — a
/// null SeriesId clears it — and an implicit default would silently wipe a
/// book's series on any partial-looking call.</summary>
public sealed record UpdateBookDetails(
    int BookId,
    string Title,
    BookCategory Category,
    string? CoverUrl,
    int? SeriesId,
    int? SeriesOrder,
    string? SeriesOrderDisplay) : ICommand;

public sealed class UpdateBookDetailsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<UpdateBookDetails>
{
    public async Task HandleAsync(UpdateBookDetails command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct)
            ?? throw new NotFoundException($"Book {command.BookId} not found.");
        book.UpdateDetails(command.Title, command.Category, command.CoverUrl);
        if (command.SeriesId is int seriesId)
            book.AssignToSeries(seriesId, command.SeriesOrder, command.SeriesOrderDisplay);
        else
            book.ClearSeries();
        await db.SaveChangesAsync(ct);
    }
}
