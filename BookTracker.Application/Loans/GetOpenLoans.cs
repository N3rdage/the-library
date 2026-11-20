using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

// Read-model for the list of copies currently lent out, ordered by borrower
// then due date (no due date last). Starting from Loans and navigating
// Copy → Edition → Book, the Book query filter hides loans whose book is
// soft-deleted (a required navigation to a filtered entity becomes an inner
// join). Author is the lead Work's first Author-role contributor; the cover is
// the edition's, falling back to the book's default.
public sealed record GetOpenLoans : IQuery<IReadOnlyList<OpenLoanRow>>;

public record OpenLoanRow(
    int LoanId, int CopyId, int BookId, string Title, string? Author, string? CoverUrl,
    string BorrowerName, DateOnly LentOn, DateOnly? DueOn);

public sealed class GetOpenLoansHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetOpenLoans, IReadOnlyList<OpenLoanRow>>
{
    public async Task<IReadOnlyList<OpenLoanRow>> HandleAsync(GetOpenLoans query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Loans
            .AsNoTracking()
            .Where(l => l.ReturnedOn == null)
            .OrderBy(l => l.BorrowerName)
            .ThenBy(l => l.DueOn == null)
            .ThenBy(l => l.DueOn)
            .ThenBy(l => l.Id)
            .Select(l => new OpenLoanRow(
                l.Id,
                l.CopyId,
                l.Copy.Edition.BookId,
                l.Copy.Edition.Book.Title,
                l.Copy.Edition.Book.BookWorks
                    .OrderBy(bw => bw.Order)
                    .ThenBy(bw => bw.WorkId)
                    .Select(bw => bw.Work.WorkAuthors
                        .Where(wa => wa.Role == AuthorRole.Author)
                        .OrderBy(wa => wa.Order)
                        .Select(wa => wa.Author.Name)
                        .FirstOrDefault())
                    .FirstOrDefault(),
                l.Copy.Edition.CoverUrl ?? l.Copy.Edition.Book.DefaultCoverArtUrl,
                l.BorrowerName,
                l.LentOn,
                l.DueOn))
            .ToListAsync(ct);
    }
}
