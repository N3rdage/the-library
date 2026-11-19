using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

// Read-model for returned loans, newest return first, at most MaxResults rows;
// BookId narrows it to one book. Like GetOpenLoans it starts from Loans and
// navigates Copy → Edition → Book, so the Book query filter hides loans whose
// book is soft-deleted.
public sealed record GetLoanHistory(int? BookId = null) : IQuery<IReadOnlyList<LoanHistoryRow>>
{
    public const int MaxResults = 200;
}

public record LoanHistoryRow(
    int LoanId, int BookId, string Title, string BorrowerName, DateOnly LentOn, DateOnly ReturnedOn);

public sealed class GetLoanHistoryHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetLoanHistory, IReadOnlyList<LoanHistoryRow>>
{
    public async Task<IReadOnlyList<LoanHistoryRow>> HandleAsync(GetLoanHistory query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var loans = db.Loans
            .AsNoTracking()
            .Where(l => l.ReturnedOn != null);
        if (query.BookId is int bookId)
            loans = loans.Where(l => l.Copy.Edition.BookId == bookId);

        return await loans
            .OrderByDescending(l => l.ReturnedOn)
            .ThenByDescending(l => l.Id)
            .Take(GetLoanHistory.MaxResults)
            .Select(l => new LoanHistoryRow(
                l.Id,
                l.Copy.Edition.BookId,
                l.Copy.Edition.Book.Title,
                l.BorrowerName,
                l.LentOn,
                l.ReturnedOn!.Value))
            .ToListAsync(ct);
    }
}
