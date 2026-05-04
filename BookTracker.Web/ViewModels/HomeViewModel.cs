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

        // "Authors" counts canonical Author entities — pen-name aliases roll
        // up under their canonical (e.g. Bachman titles count toward King).
        TotalAuthors = await db.Authors
            .Where(a => a.CanonicalAuthorId == null)
            .CountAsync();

        // Group authorship records by the canonical author id (or own id if
        // canonical), then look up the canonical name for display. Each
        // WorkAuthor row contributes one tally — co-authored works contribute
        // to BOTH credited authors (post-PR2 behaviour change vs the lead-only
        // legacy where Preston + Child only counted toward the lead).
        var authorTotals = await db.WorkAuthors
            .GroupBy(wa => wa.Author.CanonicalAuthorId ?? wa.AuthorId)
            .Select(g => new { CanonicalId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var canonicalIds = authorTotals.Select(t => t.CanonicalId).ToList();
        var nameLookup = await db.Authors
            .Where(a => canonicalIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        TopAuthors = authorTotals
            .Select(t => new AuthorCount(t.CanonicalId, nameLookup.GetValueOrDefault(t.CanonicalId) ?? "(unknown)", t.Count))
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Author)
            .ToList();

        var genres = await db.Genres
            .Select(g => new { Genre = g.Name, Count = g.Works.Count })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Genre)
            .Take(10)
            .ToListAsync();
        TopGenres = genres.Select(x => new GenreCount(x.Genre, x.Count)).ToList();

        MaxAuthor = TopAuthors.Count > 0 ? TopAuthors.Max(a => a.Count) : 0;
        MaxGenre = TopGenres.Count > 0 ? TopGenres.Max(g => g.Count) : 0;
    }

    public record AuthorCount(int CanonicalAuthorId, string Author, int Count);
    public record GenreCount(string Genre, int Count);
}
