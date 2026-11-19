using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Loans;

// Borrower-name suggestions for the lend dialog: distinct names from every loan
// (open and returned), most recently lent first, filtered by a case-insensitive
// prefix when one is given, at most MaxResults. Names group as the database
// collation compares them (case-insensitive by default), so "sam" and "Sam"
// are one suggestion; which spelling it carries is the database's choice.
public sealed record GetBorrowerNames(string? Prefix) : IQuery<IReadOnlyList<string>>
{
    public const int MaxResults = 20;
}

public sealed class GetBorrowerNamesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetBorrowerNames, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> HandleAsync(GetBorrowerNames query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var loans = db.Loans.AsNoTracking();

        var prefix = query.Prefix?.Trim();
        if (!string.IsNullOrEmpty(prefix))
        {
            var lowered = prefix.ToLower();
            loans = loans.Where(l => l.BorrowerName.ToLower().StartsWith(lowered));
        }

        // Group in SQL for the distinct names with their latest lend. The
        // in-memory Distinct only matters under a case-sensitive collation.
        var latest = await loans
            .GroupBy(l => l.BorrowerName)
            .Select(g => new { Name = g.Key, LastLentOn = g.Max(l => l.LentOn), LastId = g.Max(l => l.Id) })
            .OrderByDescending(g => g.LastLentOn)
            .ThenByDescending(g => g.LastId)
            .Take(GetBorrowerNames.MaxResults)
            .ToListAsync(ct);

        return latest
            .Select(g => g.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
