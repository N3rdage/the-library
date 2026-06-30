using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Repositions a Book within its current series (the inline order edit
/// on the series page). The free-text label ("4.5") arrives already parsed into
/// an integer sort key + optional display override — the VM owns the parsing.
/// Membership is untouched. Soft no-op if the Book is gone.</summary>
public sealed record SetBookSeriesOrder(int BookId, int? Order, string? OrderDisplay) : ICommand;

public sealed class SetBookSeriesOrderHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<SetBookSeriesOrder>
{
    public async Task HandleAsync(SetBookSeriesOrder command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books.FindAsync([command.BookId], ct);
        if (book is null) return;

        book.SetSeriesOrder(command.Order, command.OrderDisplay);
        await db.SaveChangesAsync(ct);
    }
}
