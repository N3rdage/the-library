using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Dialog-scoped VM for "Edit work" on the View page. Edits a single
// Work's title, subtitle, author (find-or-create with typeahead), first
// published date (PartialDateParser), and series membership + order.
// Genres stay on the full /edit page in this PR — the hierarchical
// MudBlazor rebuild is tracked separately.
public class WorkEditDialogViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool NotFound { get; private set; }
    public int WorkId { get; private set; }

    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string AuthorName { get; set; } = "";
    public string FirstPublishedDate { get; set; } = "";
    public int? SelectedSeriesId { get; set; }
    public int? SeriesOrder { get; set; }

    public List<SeriesOption> AvailableSeries { get; private set; } = [];

    public async Task InitializeAsync(int workId)
    {
        WorkId = workId;

        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works.Include(w => w.Author).FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) { NotFound = true; return; }

        Title = work.Title;
        Subtitle = work.Subtitle;
        AuthorName = work.Author.Name;
        FirstPublishedDate = PartialDateParser.Format(work.FirstPublishedDate, work.FirstPublishedDatePrecision);
        SelectedSeriesId = work.SeriesId;
        SeriesOrder = work.SeriesOrder;

        AvailableSeries = await db.Series
            .OrderBy(s => s.Name)
            .Select(s => new SeriesOption(s.Id, s.Name, s.Type))
            .ToListAsync();
    }

    public async Task<IEnumerable<string>> SearchAuthorsAsync(string query, CancellationToken ct)
    {
        var q = (query ?? "").Trim();
        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = db.Authors.AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            matches = matches.Where(a => a.Name.Contains(q));
        }

        return await matches
            .OrderBy(a => a.Name)
            .Select(a => a.Name)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task SaveAsync()
    {
        if (NotFound || string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(AuthorName)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works.Include(w => w.Author).FirstOrDefaultAsync(w => w.Id == WorkId);
        if (work is null) return;

        work.Title = Title.Trim();
        work.Subtitle = string.IsNullOrWhiteSpace(Subtitle) ? null : Subtitle.Trim();
        work.Author = await AuthorResolver.FindOrCreateAsync(AuthorName, db);

        var parsed = PartialDateParser.TryParse(FirstPublishedDate) ?? PartialDate.Empty;
        work.FirstPublishedDate = parsed.Date;
        work.FirstPublishedDatePrecision = parsed.Precision;

        work.SeriesId = SelectedSeriesId;
        work.SeriesOrder = SelectedSeriesId.HasValue ? SeriesOrder : null;

        await db.SaveChangesAsync();
    }

    public record SeriesOption(int Id, string Name, SeriesType Type);
}
