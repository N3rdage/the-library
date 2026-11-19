using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

// Read-model for Home's "Due back" panel: the open loans that have a due date,
// soonest first (then by loan id), at most MaxDated, plus a count of the open
// loans without one. Loans whose book is soft-deleted are left out of both,
// the same as GetOpenLoans.
public sealed record GetDueBack : IQuery<DueBackSummary>
{
    public const int MaxDated = 10;
}

public record DueBackSummary(IReadOnlyList<DueBackRow> Dated, int OpenEndedCount);

public record DueBackRow(int LoanId, int BookId, string Title, string BorrowerName, DateOnly DueOn);

public sealed class GetDueBackHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetDueBack, DueBackSummary>
{
    public async Task<DueBackSummary> HandleAsync(GetDueBack query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var open = db.Loans
            .AsNoTracking()
            .Where(l => l.ReturnedOn == null && l.Copy.Edition.Book.DeletedAt == null);

        var dated = await open
            .Where(l => l.DueOn != null)
            .OrderBy(l => l.DueOn)
            .ThenBy(l => l.Id)
            .Take(GetDueBack.MaxDated)
            .Select(l => new DueBackRow(
                l.Id,
                l.Copy.Edition.BookId,
                l.Copy.Edition.Book.Title,
                l.BorrowerName,
                l.DueOn!.Value))
            .ToListAsync(ct);

        var openEnded = await open.CountAsync(l => l.DueOn == null, ct);

        return new DueBackSummary(dated, openEnded);
    }
}
