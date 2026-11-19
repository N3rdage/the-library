using BookTracker.Application.Formatting;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Shelf;

// Read-model for the /shelf-order page: every live book, grouped by top-level
// genre and put in shelving order (ShelfOrderKey) inside each group, so the
// list can be read at the bookcase top to bottom.
//
// A book is placed by its lead Work (lowest BookWork.Order). That Work's genres
// are walked up Genre.ParentGenre to their roots; the alphabetically first root
// is the book's group, and any other roots are listed as AlsoIn so the page can
// show where else it could sit. A book whose lead Work has no genre goes to the
// last group, "Unsorted". The lead author is the Work's first Author-role
// credit (falling back to the first contributor of any role for an edited
// anthology), named as published (a Bachman book shelves under Bachman).
public sealed record GetShelfOrder : IQuery<ShelfOrderView>;

public sealed record ShelfOrderView(
    int TotalBooks,
    int BooksWithSeveralGenres,
    IReadOnlyList<ShelfGenreGroup> Groups);

public sealed record ShelfGenreGroup(string Genre, IReadOnlyList<ShelfBook> Books);

public sealed record ShelfBook(
    int BookId,
    string Title,
    string? ShelvedAs,
    string? SeriesName,
    string? SeriesOrderLabel,
    int? FirstPublishedYear,
    IReadOnlyList<string> AlsoIn);

public sealed class GetShelfOrderHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetShelfOrder, ShelfOrderView>
{
    public const string UnsortedGroup = "Unsorted";

    private static readonly StringComparer Text = StringComparer.InvariantCultureIgnoreCase;

    public async Task<ShelfOrderView> HandleAsync(GetShelfOrder query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var books = await db.Books
            .AsNoTracking()
            .Select(b => new
            {
                b.Id,
                b.Title,
                SeriesName = b.Series == null ? null : b.Series.Name,
                b.SeriesOrder,
                b.SeriesOrderDisplay,
            })
            .ToListAsync(ct);

        // One row per Book↔Work link with the Work's scalars; the lead Work is
        // picked per book in memory (lowest Order, then WorkId as a stable tiebreak).
        var links = await db.Set<BookWork>()
            .AsNoTracking()
            .Select(bw => new
            {
                bw.BookId,
                bw.WorkId,
                bw.Order,
                bw.Work.FirstPublishedDate,
                LeadAuthor = bw.Work.WorkAuthors
                    .OrderBy(wa => wa.Role == AuthorRole.Author ? 0 : 1)
                    .ThenBy(wa => wa.Role)
                    .ThenBy(wa => wa.Order)
                    .Select(wa => wa.Author.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        var leadWorkByBook = links
            .GroupBy(l => l.BookId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Order).ThenBy(l => l.WorkId).First());

        var workGenres = await db.Works
            .AsNoTracking()
            .SelectMany(w => w.Genres.Select(g => new { WorkId = w.Id, GenreId = g.Id }))
            .ToListAsync(ct);
        var genreIdsByWork = workGenres.ToLookup(x => x.WorkId, x => x.GenreId);

        var genres = await db.Genres
            .AsNoTracking()
            .Select(g => new { g.Id, g.Name, g.ParentGenreId })
            .ToDictionaryAsync(g => g.Id, ct);

        string RootName(int genreId)
        {
            var current = genres[genreId];
            // Bounded walk: a parent cycle in bad data cannot loop forever.
            for (var steps = 0; current.ParentGenreId is int parentId && steps < genres.Count; steps++)
                current = genres[parentId];
            return current.Name;
        }

        var placed = books.Select(b =>
        {
            var lead = leadWorkByBook.GetValueOrDefault(b.Id);
            var roots = lead is null
                ? []
                : genreIdsByWork[lead.WorkId]
                    .Select(RootName)
                    .Distinct(Text)
                    .Order(Text)
                    .ToList();
            var key = ShelfOrderKey.For(
                lead?.LeadAuthor, b.SeriesName, b.SeriesOrder, lead?.FirstPublishedDate, b.Title);
            var row = new ShelfBook(
                b.Id,
                b.Title,
                lead?.LeadAuthor is { } name && !string.IsNullOrWhiteSpace(name) ? ShelfOrderKey.ShelvedAs(name) : null,
                b.SeriesName,
                SeriesOrderParser.Format(b.SeriesOrder, b.SeriesOrderDisplay),
                lead?.FirstPublishedDate?.Year,
                roots.Skip(1).ToList());
            return (Group: roots.Count > 0 ? roots[0] : null, Key: key, Row: row);
        }).ToList();

        var groups = placed
            .GroupBy(p => p.Group, Text)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key, Text)
            .Select(g => new ShelfGenreGroup(
                g.Key ?? UnsortedGroup,
                g.OrderBy(p => p.Key).Select(p => p.Row).ToList()))
            .ToList();

        return new ShelfOrderView(
            placed.Count,
            placed.Count(p => p.Row.AlsoIn.Count > 0),
            groups);
    }
}
