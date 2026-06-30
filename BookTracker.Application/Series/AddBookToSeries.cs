using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Adds a Book to a Series from the series-management page, landing it
/// at the end of the running order (one past the current max). Routes through the
/// Book aggregate, which owns its series membership. Soft no-op if the Book was
/// deleted between picking it and saving.</summary>
public sealed record AddBookToSeries(int SeriesId, int BookId) : ICommand;

public sealed class AddBookToSeriesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddBookToSeries>
{
    public async Task HandleAsync(AddBookToSeries command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct);
        if (book is null) return; // gone between pick + save

        // Next slot = one past the highest order already in the series (1-based),
        // so the book appends to the end. Books with a null order count as 0, so
        // a series of all-unordered books yields 1.
        var maxOrder = await db.Books
            .Where(b => b.SeriesId == command.SeriesId)
            .MaxAsync(b => (int?)b.SeriesOrder, ct) ?? 0;

        book.AssignToSeries(command.SeriesId, maxOrder + 1, orderDisplay: null);
        await db.SaveChangesAsync(ct);
    }
}
