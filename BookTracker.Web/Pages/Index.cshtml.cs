using BookTracker.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Pages;

public class IndexModel(BookTrackerDbContext db) : PageModel
{
    public int TotalBooks { get; private set; }
    public int TotalAuthors { get; private set; }
    public IReadOnlyList<AuthorCount> TopAuthors { get; private set; } = [];
    public IReadOnlyList<GenreCount> TopGenres { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        TotalBooks = await db.Books.CountAsync(ct);

        TotalAuthors = await db.Books
            .Select(b => b.Author)
            .Distinct()
            .CountAsync(ct);

        var topAuthors = await db.Books
            .GroupBy(b => b.Author)
            .Select(g => new { Author = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Author)
            .Take(10)
            .ToListAsync(ct);
        TopAuthors = topAuthors.Select(x => new AuthorCount(x.Author, x.Count)).ToList();

        var topGenres = await db.Books
            .Where(b => b.Genre != null && b.Genre != "")
            .GroupBy(b => b.Genre!)
            .Select(g => new { Genre = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Genre)
            .Take(10)
            .ToListAsync(ct);
        TopGenres = topGenres.Select(x => new GenreCount(x.Genre, x.Count)).ToList();
    }

    public record AuthorCount(string Author, int Count);
    public record GenreCount(string Genre, int Count);
}
