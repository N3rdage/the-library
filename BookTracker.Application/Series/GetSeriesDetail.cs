using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Series;

// Read-model that prefills the Series edit page: the form fields and the
// series' books ordered by SeriesOrder (unordered last), then title. Null when
// the series is gone. Relocated from SeriesEditViewModel's tracked
// Include(Books → Works → WorkAuthors → Author) load (#265).
public sealed record GetSeriesDetail(int SeriesId) : IQuery<SeriesDetail?>;

public record SeriesDetail(
    int Id,
    string Name,
    string? Author,
    SeriesType Type,
    int? ExpectedCount,
    string? Description,
    IReadOnlyList<SeriesBookRow> Books);

public record SeriesBookRow(int Id, string Title, string Author, int? SeriesOrder, string? SeriesOrderDisplay);

public sealed class GetSeriesDetailHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetSeriesDetail, SeriesDetail?>
{
    public async Task<SeriesDetail?> HandleAsync(GetSeriesDetail query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var series = await db.Series
            .AsNoTracking()
            .Where(s => s.Id == query.SeriesId)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Author,
                s.Type,
                s.ExpectedCount,
                s.Description,
                Books = s.Books
                    .Select(b => new
                    {
                        b.Id,
                        b.Title,
                        b.SeriesOrder,
                        b.SeriesOrderDisplay,
                        AuthorsPerWork = b.Works
                            .Select(w => w.WorkAuthors
                                .Where(wa => wa.Role == AuthorRole.Author)
                                .OrderBy(wa => wa.Order)
                                .Select(wa => wa.Author.Name)
                                .ToList())
                            .ToList(),
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (series is null) return null;

        // Ordered in memory, as before: by order (unordered last), then title.
        var books = series.Books
            .OrderBy(b => b.SeriesOrder ?? int.MaxValue)
            .ThenBy(b => b.Title)
            .Select(b => new SeriesBookRow(
                b.Id, b.Title, SeriesBookAuthors.Display(b.AuthorsPerWork), b.SeriesOrder, b.SeriesOrderDisplay))
            .ToList();

        return new SeriesDetail(
            series.Id, series.Name, series.Author, series.Type, series.ExpectedCount, series.Description, books);
    }
}

// A book's author display on the series page = distinct Author-role names
// across its Works, lead-first within each Work, comma-joined (the Library
// list's join; the " & " formatter is reserved for single-Work detail
// surfaces). Shared by GetSeriesDetail, GetSeriesBookRow and
// SearchBooksForSeries so a book reads the same in the search dropdown and in
// the table once added. The handlers project the Author-role names per Work,
// already ordered by credit order.
internal static class SeriesBookAuthors
{
    public static string Display(IEnumerable<IEnumerable<string>> authorsPerWork) =>
        string.Join(", ", authorsPerWork.SelectMany(names => names).Distinct());
}
