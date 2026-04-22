using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Services;

public interface IWorkSearchService
{
    Task<IReadOnlyList<WorkSearchResult>> SearchAsync(
        string query,
        int? excludeBookId = null,
        int maxResults = 20,
        CancellationToken ct = default);
}

public record WorkSearchResult(
    int Id,
    string Title,
    string? Subtitle,
    string AuthorName,
    int BookCount);

public class WorkSearchService(IDbContextFactory<BookTrackerDbContext> dbFactory) : IWorkSearchService
{
    public async Task<IReadOnlyList<WorkSearchResult>> SearchAsync(
        string query,
        int? excludeBookId = null,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return [];
        }

        var q = query.Trim().ToLower();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Exclude Works already attached to the given Book so the caller
        // doesn't get "attach this Work that's already here" options in
        // the dropdown.
        var worksQuery = db.Works.AsNoTracking();
        if (excludeBookId is int excludeId)
        {
            worksQuery = worksQuery.Where(w => !w.Books.Any(b => b.Id == excludeId));
        }

        // Server-evaluable substring + case filter. SQL Server default
        // collations are case-insensitive, but ToLower() + Contains keeps
        // the InMemory provider honest under tests.
        var matches = await worksQuery
            .Where(w => w.Title.ToLower().Contains(q))
            .Select(w => new
            {
                w.Id,
                w.Title,
                w.Subtitle,
                AuthorName = w.Author.Name,
                BookCount = w.Books.Count,
                TitleLower = w.Title.ToLower()
            })
            .Take(maxResults * 3)
            .ToListAsync(ct);

        // Rank in memory: starts-with first, then contains anywhere,
        // alphabetical within each group.
        return matches
            .OrderByDescending(m => m.TitleLower.StartsWith(q))
            .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(m => new WorkSearchResult(m.Id, m.Title, m.Subtitle, m.AuthorName, m.BookCount))
            .ToList();
    }
}
