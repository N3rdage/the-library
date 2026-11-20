using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

/// <summary>Lends one or more copies to one borrower in one gesture. All or
/// nothing: a missing copy throws <see cref="NotFoundException"/>, an
/// already-lent copy (or a blank name / due-before-lent) throws the entity's
/// DomainRuleException, and in either case nothing is saved. One SaveChanges,
/// so the loans land in one transaction.</summary>
public sealed record LendCopies(
    IReadOnlyList<int> CopyIds, string BorrowerName, DateOnly LentOn, DateOnly? DueOn) : ICommand;

public sealed class LendCopiesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<LendCopies>
{
    public async Task HandleAsync(LendCopies command, CancellationToken ct = default)
    {
        var ids = command.CopyIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new ArgumentException("At least one copy is required.", nameof(command));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Loans loaded so Copy.LendTo sees an existing open loan.
        var copies = await db.Copies
            .Include(c => c.Loans)
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);

        var missing = ids.Except(copies.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException($"Copy {string.Join(", ", missing)} not found.");

        foreach (var copy in copies)
            copy.LendTo(command.BorrowerName, command.LentOn, command.DueOn);

        await db.SaveChangesAsync(ct);
    }
}
