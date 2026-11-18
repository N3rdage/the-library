using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Works;

// Read-model that prefills the Edit work dialog: title, subtitle, the Author-
// role names in chip order, the other contributors (by role, then order), the
// first-published date raw (date + precision) and the selected genre ids. Null
// when the work is gone. Relocated from WorkEditDialogViewModel's tracked
// Include(WorkAuthors).Include(Genres) load (#264).
public sealed record GetWorkForEdit(int WorkId) : IQuery<WorkForEdit?>;

public record WorkForEdit(
    int Id,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> AuthorNames,
    IReadOnlyList<WorkContributorView> Contributors,
    DateOnly? FirstPublishedDate,
    DatePrecision FirstPublishedDatePrecision,
    IReadOnlyList<int> GenreIds);

public record WorkContributorView(string Name, AuthorRole Role);

public sealed class GetWorkForEditHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetWorkForEdit, WorkForEdit?>
{
    public async Task<WorkForEdit?> HandleAsync(GetWorkForEdit query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Works
            .AsNoTracking()
            .Where(w => w.Id == query.WorkId)
            .Select(w => new
            {
                w.Id,
                w.Title,
                w.Subtitle,
                w.FirstPublishedDate,
                w.FirstPublishedDatePrecision,
                Credits = w.WorkAuthors
                    .Select(wa => new { wa.Author.Name, wa.Role, wa.Order })
                    .ToList(),
                GenreIds = w.Genres.Select(g => g.Id).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        // Ordered in memory over the handful of credit rows: the lead author
        // first in the chip list; contributors grouped by role, then order.
        var authorNames = row.Credits
            .Where(c => c.Role == AuthorRole.Author)
            .OrderBy(c => c.Order)
            .Select(c => c.Name)
            .ToList();
        var contributors = row.Credits
            .Where(c => c.Role != AuthorRole.Author)
            .OrderBy(c => (int)c.Role)
            .ThenBy(c => c.Order)
            .Select(c => new WorkContributorView(c.Name, c.Role))
            .ToList();

        return new WorkForEdit(
            row.Id, row.Title, row.Subtitle, authorNames, contributors,
            row.FirstPublishedDate, row.FirstPublishedDatePrecision, row.GenreIds);
    }
}
