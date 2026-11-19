using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

/// <summary>Marks a loan returned on the given date. The loan stays as history.
/// Throws <see cref="NotFoundException"/> when the loan is gone, and the
/// entity's DomainRuleException when it is already returned or the date is
/// before the lend date.</summary>
public sealed record ReturnLoan(int LoanId, DateOnly ReturnedOn) : ICommand;

public sealed class ReturnLoanHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<ReturnLoan>
{
    public async Task HandleAsync(ReturnLoan command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Load the owning Copy with all its Loans and close through the Copy,
        // which owns the open-loan rule.
        var copy = await db.Copies
            .Include(c => c.Loans)
            .FirstOrDefaultAsync(c => c.Loans.Any(l => l.Id == command.LoanId), ct)
            ?? throw new NotFoundException($"Loan {command.LoanId} not found.");

        if (copy.OpenLoan?.Id != command.LoanId)
            throw new DomainRuleException("This loan has already been returned.");

        copy.MarkReturned(command.ReturnedOn);
        await db.SaveChangesAsync(ct);
    }
}
