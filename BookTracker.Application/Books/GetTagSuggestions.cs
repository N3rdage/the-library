using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Read-model for the BookDetail tag autocomplete. Relocated from
// BookDetailViewModel.SearchTagsAsync in PR6b-3. Returns up to 20 existing tag
// names matching the (case-insensitive) substring, ordered by name. The
// "exclude tags already on this book" filter stays in the VM, which owns the
// current-tags state.
public sealed record GetTagSuggestions(string Query) : IQuery<IReadOnlyList<string>>;

public sealed class GetTagSuggestionsHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetTagSuggestions, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> HandleAsync(GetTagSuggestions query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Same normalisation the resolver stores by, so a query matches the
        // (lower-cased) stored names (TagResolver is the single owner).
        var q = TagResolver.Normalize(query.Query);

        return await db.Tags
            .AsNoTracking()
            .Where(t => string.IsNullOrEmpty(q) || t.Name.Contains(q))
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .Take(20)
            .ToListAsync(ct);
    }
}
