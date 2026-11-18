using BookTracker.Application.Genres;
using BookTracker.Application.Wishlist;
using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Ai;

// Read-model for the AI shopping-list prompt's library profile. Four facts:
//   TopAuthors  — the 15 most-credited author names, each with its credit
//                 count and the average rating of the books its works appear in.
//   TopGenres   — the 10 most-used genres (shared GenreReads).
//   HighlyRated — up to 20 books rated 4 or higher, best first, then by title.
//   SeriesGaps  — structured series with missing numbered slots, from
//                 GetSeriesGaps, so the prompt and the Gaps page apply one rule.
//
// Author stats tally one credit per WorkAuthor row by the author's own name:
// a co-authored work counts for every credited author, and pen names stay
// separate (no canonical rollup; the prompt keeps the published names). The
// tally is role-blind, as are the highly-rated books' author names (through
// the Work.Authors skip navigation): an editor or translator counts as an
// author.
public sealed record GetAiLibraryProfile : IQuery<AiLibraryProfile>;

public record AiLibraryProfile(
    IReadOnlyList<AiAuthorStat> TopAuthors,
    IReadOnlyList<AiGenreCount> TopGenres,
    IReadOnlyList<AiRatedBook> HighlyRated,
    IReadOnlyList<AiSeriesGap> SeriesGaps);

public record AiAuthorStat(string Author, int Count, double AvgRating);

public record AiGenreCount(string Name, int Count);

public record AiRatedBook(string Title, string Author, int Rating);

public record AiSeriesGap(
    string SeriesName, string? Author,
    int OwnedCount, int ExpectedCount,
    IReadOnlyList<int> MissingPositions);

public sealed class GetAiLibraryProfileHandler(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IQueryHandler<GetSeriesGaps, SeriesGapsResult> seriesGaps)
    : IQueryHandler<GetAiLibraryProfile, AiLibraryProfile>
{
    public async Task<AiLibraryProfile> HandleAsync(GetAiLibraryProfile query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var authorRows = await db.WorkAuthors
            .AsNoTracking()
            .GroupBy(wa => wa.Author.Name)
            .Select(g => new
            {
                Author = g.Key,
                Count = g.Count(),
                AvgRating = g.SelectMany(wa => wa.Work.Books).Average(b => (double?)b.Rating) ?? 0.0,
            })
            .OrderByDescending(a => a.Count)
            .Take(15)
            .ToListAsync(ct);
        var topAuthors = authorRows
            .Select(a => new AiAuthorStat(a.Author, a.Count, a.AvgRating))
            .ToList();

        var topGenres = (await GenreReads.TopGenresAsync(db, 10, ct))
            .Select(g => new AiGenreCount(g.Name, g.Count))
            .ToList();

        var highlyRated = await db.Books
            .AsNoTracking()
            .Where(b => b.Rating >= 4)
            .OrderByDescending(b => b.Rating).ThenBy(b => b.Title)
            .Take(20)
            .Select(b => new AiRatedBook(
                b.Title,
                string.Join(", ", b.Works.SelectMany(w => w.Authors.Select(a => a.Name)).Distinct()),
                b.Rating))
            .ToListAsync(ct);

        var gaps = (await seriesGaps.HandleAsync(new GetSeriesGaps(), ct)).Gaps
            .Select(g => new AiSeriesGap(g.SeriesName, g.Author, g.OwnedCount, g.ExpectedCount, g.MissingPositions))
            .ToList();

        return new AiLibraryProfile(topAuthors, topGenres, highlyRated, gaps);
    }
}
