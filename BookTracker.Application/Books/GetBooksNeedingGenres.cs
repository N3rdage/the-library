using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

/// <summary>The AI assistant's genre-cleanup list: the 50 books with the fewest
/// genres (across all their works), then by title. Relocated from
/// AIAssistantViewModel.LoadBooksNeedingGenresAsync.</summary>
public sealed record GetBooksNeedingGenres : IQuery<List<BookGenreRow>>;

/// <summary>One row of the genre-cleanup list. Subtitle and Author come from
/// the book's first Work (lead Author-role name only — the AI prompt takes a
/// single author string); CurrentGenres is the distinct genre names across all
/// the book's works.</summary>
public sealed record BookGenreRow(
    int Id, string Title, string? Subtitle, string Author,
    List<string> CurrentGenres);

public sealed class GetBooksNeedingGenresHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetBooksNeedingGenres, List<BookGenreRow>>
{
    public async Task<List<BookGenreRow>> HandleAsync(GetBooksNeedingGenres query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Genre suggestions operate on the book's primary work — single-work
        // books are the common case. The "first Work" is a best-effort
        // representative, so it stays on the plain Works skip-nav rather than
        // the ordered BookWorks projection: order is immaterial to a
        // single-work-focused prompt.
        return await db.Books
            .AsNoTracking()
            .OrderBy(b => b.Works.SelectMany(w => w.Genres).Count())
            .ThenBy(b => b.Title)
            .Take(50)
            .Select(b => new BookGenreRow(
                b.Id,
                b.Title,
                b.Works.FirstOrDefault()!.Subtitle,
                b.Works.FirstOrDefault()!.WorkAuthors
                    .Where(wa => wa.Role == AuthorRole.Author)
                    .OrderBy(wa => wa.Order)
                    .Select(wa => wa.Author.Name)
                    .FirstOrDefault() ?? "",
                b.Works.SelectMany(w => w.Genres).Select(g => g.Name).Distinct().ToList()))
            .ToListAsync(ct);
    }
}
