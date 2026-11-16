using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Authors;

// Read-model for the author and contributor pickers' typeahead: author names
// (pen names included) matching a case-insensitive substring, by name, at most
// 20. Names in Exclude are left out (the author picker passes the chips already
// chosen; the contributor picker passes an empty list). Relocated from the
// inline DbContext reads in MudAuthorPicker and MudContributorPicker (#264).
public sealed record SearchAuthorNames(string? Query, IReadOnlyCollection<string> Exclude)
    : IQuery<IReadOnlyList<string>>;

public sealed class SearchAuthorNamesHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<SearchAuthorNames, IReadOnlyList<string>>
{
    public const int MaxResults = 20;

    public async Task<IReadOnlyList<string>> HandleAsync(SearchAuthorNames query, CancellationToken ct = default)
    {
        // Lowercased on both sides so the match is case-insensitive whatever the
        // column collation.
        var q = (query.Query ?? "").Trim().ToLower();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var authors = db.Authors.AsNoTracking();
        if (!string.IsNullOrEmpty(q))
        {
            authors = authors.Where(a => a.Name.ToLower().Contains(q));
        }

        var names = authors.OrderBy(a => a.Name).Select(a => a.Name);
        if (query.Exclude.Count > 0)
        {
            var exclude = query.Exclude.ToList();
            names = names.Where(n => !exclude.Contains(n));
        }

        return await names.Take(MaxResults).ToListAsync(ct);
    }
}
