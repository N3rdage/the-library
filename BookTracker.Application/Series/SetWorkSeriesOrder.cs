using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

/// <summary>Repositions a Work within its current series (the inline order edit
/// on the series page). The free-text label ("4.5") arrives already parsed into
/// an integer sort key + optional display override — the VM owns the parsing,
/// matching <c>UpdateWork</c>. Membership is untouched. Soft no-op if the Work is
/// gone.</summary>
public sealed record SetWorkSeriesOrder(int WorkId, int? Order, string? OrderDisplay) : ICommand;

public sealed class SetWorkSeriesOrderHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<SetWorkSeriesOrder>
{
    public async Task HandleAsync(SetWorkSeriesOrder command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var work = await db.Works.FindAsync([command.WorkId], ct);
        if (work is null) return;

        work.SetSeriesOrder(command.Order, command.OrderDisplay);
        await db.SaveChangesAsync(ct);
    }
}
