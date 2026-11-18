using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>Which of these ISBNs does the library hold as an Edition? Returns
/// ISBN to <c>BookId</c> for each live Edition whose ISBN is in the list,
/// keyed case-insensitively by the stored ISBN; ISBNs the library does not
/// hold are absent. The Book soft-delete filter hides tombstoned Books (whose
/// Editions are removed on delete anyway).</summary>
public sealed record FindOwnedEditions(IReadOnlyList<string> Isbns) : IQuery<IReadOnlyDictionary<string, int>>;

public sealed class FindOwnedEditionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<FindOwnedEditions, IReadOnlyDictionary<string, int>>
{
    public async Task<IReadOnlyDictionary<string, int>> HandleAsync(
        FindOwnedEditions query, CancellationToken ct = default)
    {
        var owned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var isbns = query.Isbns
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (isbns.Count == 0) return owned;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.Editions
            .AsNoTracking()
            .Where(e => e.Isbn != null && isbns.Contains(e.Isbn))
            .Where(e => e.Book.DeletedAt == null)
            .Select(e => new { Isbn = e.Isbn!, e.BookId })
            .ToListAsync(ct);

        // Isbn is filtered-unique, so each appears once; TryAdd keeps the
        // first should the index ever be relaxed.
        foreach (var row in rows.OrderBy(r => r.BookId))
            owned.TryAdd(row.Isbn, row.BookId);
        return owned;
    }
}
