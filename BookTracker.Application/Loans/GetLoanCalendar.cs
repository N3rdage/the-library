using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

// Read-model behind the "Add to calendar" downloads: the open loans with a due
// date, narrowed to one loan (LoanId) or to one borrower's loans (Borrower,
// matched by name), soonest first. A returned loan, a loan without a due date
// and a loan whose book is soft-deleted never appear, so an empty list means
// there is nothing to put in a calendar.
public sealed record GetLoanCalendar(int? LoanId, string? Borrower) : IQuery<IReadOnlyList<LoanCalendarEntry>>;

public record LoanCalendarEntry(int LoanId, string Title, string BorrowerName, DateOnly DueOn);

public sealed class GetLoanCalendarHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetLoanCalendar, IReadOnlyList<LoanCalendarEntry>>
{
    public async Task<IReadOnlyList<LoanCalendarEntry>> HandleAsync(GetLoanCalendar query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var loans = db.Loans
            .AsNoTracking()
            .Where(l => l.ReturnedOn == null && l.DueOn != null && l.Copy.Edition.Book.DeletedAt == null);

        if (query.LoanId is { } loanId)
            loans = loans.Where(l => l.Id == loanId);
        if (query.Borrower is { } borrower)
            loans = loans.Where(l => l.BorrowerName == borrower);

        return await loans
            .OrderBy(l => l.DueOn)
            .ThenBy(l => l.Id)
            .Select(l => new LoanCalendarEntry(
                l.Id,
                l.Copy.Edition.Book.Title,
                l.BorrowerName,
                l.DueOn!.Value))
            .ToListAsync(ct);
    }
}
