using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class HomeViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public int TotalBooks { get; private set; }
    public int TotalAuthors { get; private set; }
    public List<AuthorCount> TopAuthors { get; private set; } = [];
    public List<GenreCount> TopGenres { get; private set; } = [];
    public int MaxAuthor { get; private set; }
    public int MaxGenre { get; private set; }

    public async Task InitializeAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        TotalBooks = await db.Books.CountAsync();

        TotalAuthors = await db.Books
            .Select(b => b.Author)
            .Distinct()
            .CountAsync();

        var authors = await db.Books
            .GroupBy(b => b.Author)
            .Select(g => new { Author = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Author)
            .Take(10)
            .ToListAsync();
        TopAuthors = authors.Select(x => new AuthorCount(x.Author, x.Count)).ToList();

        var genres = await db.Genres
            .Select(g => new { Genre = g.Name, Count = g.Books.Count })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Genre)
            .Take(10)
            .ToListAsync();
        TopGenres = genres.Select(x => new GenreCount(x.Genre, x.Count)).ToList();

        MaxAuthor = TopAuthors.Count > 0 ? TopAuthors.Max(a => a.Count) : 0;
        MaxGenre = TopGenres.Count > 0 ? TopGenres.Max(g => g.Count) : 0;
    }

    public record AuthorCount(string Author, int Count);
    public record GenreCount(string Genre, int Count);
}
