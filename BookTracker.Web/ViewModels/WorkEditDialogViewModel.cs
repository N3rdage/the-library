using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Dialog-scoped VM for "Edit work" on the View page. Edits a single
// Work's title, subtitle, author (find-or-create with typeahead), first
// published date (PartialDateParser), series membership + order, and
// genres (via MudGenrePicker — PR B).
public class WorkEditDialogViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool NotFound { get; private set; }
    public int WorkId { get; private set; }

    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    // Multi-author chip list — populated from WorkAuthors on init, dual-
    // written on save. Lead = AuthorNames[0] for legacy Work.AuthorId compat
    // during the PR1/PR2 cutover.
    public List<string> AuthorNames { get; set; } = [];
    public string FirstPublishedDate { get; set; } = "";
    public int? SelectedSeriesId { get; set; }
    public int? SeriesOrder { get; set; }
    public List<int> SelectedGenreIds { get; set; } = [];

    public List<SeriesOption> AvailableSeries { get; private set; } = [];

    public async Task InitializeAsync(int workId)
    {
        WorkId = workId;

        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works
            .Include(w => w.Author)
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Genres)
            .FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) { NotFound = true; return; }

        Title = work.Title;
        Subtitle = work.Subtitle;
        // Order ascending so the lead author shows first in the chip list.
        // Fallback to the legacy single Author if WorkAuthors is empty —
        // shouldn'\''t happen post-backfill but defensive.
        AuthorNames = work.WorkAuthors.Count > 0
            ? work.WorkAuthors.OrderBy(wa => wa.Order).Select(wa => wa.Author.Name).ToList()
            : [work.Author.Name];
        FirstPublishedDate = PartialDateParser.Format(work.FirstPublishedDate, work.FirstPublishedDatePrecision);
        SelectedSeriesId = work.SeriesId;
        SeriesOrder = work.SeriesOrder;
        SelectedGenreIds = work.Genres.Select(g => g.Id).ToList();

        AvailableSeries = await db.Series
            .OrderBy(s => s.Name)
            .Select(s => new SeriesOption(s.Id, s.Name, s.Type))
            .ToListAsync();
    }

    public async Task<IEnumerable<string>> SearchAuthorsAsync(string query, CancellationToken ct)
    {
        // Lowercased client-side before the LINQ Where so the EF InMemory
        // provider (case-sensitive) and SQL Server (case-insensitive by
        // default collation) agree on substring matching.
        var q = (query ?? "").Trim().ToLower();
        await using var db = await dbFactory.CreateDbContextAsync();
        var matches = db.Authors.AsQueryable();
        if (!string.IsNullOrEmpty(q))
        {
            matches = matches.Where(a => a.Name.ToLower().Contains(q));
        }

        return await matches
            .OrderBy(a => a.Name)
            .Select(a => a.Name)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task SaveAsync()
    {
        if (NotFound || string.IsNullOrWhiteSpace(Title) || AuthorNames.All(string.IsNullOrWhiteSpace)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works
            .Include(w => w.Author)
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Genres)
            .FirstOrDefaultAsync(w => w.Id == WorkId);
        if (work is null) return;

        work.Title = Title.Trim();
        work.Subtitle = string.IsNullOrWhiteSpace(Subtitle) ? null : Subtitle.Trim();

        var authors = await AuthorResolver.FindOrCreateAllAsync(AuthorNames, db);
        if (authors.Count == 0) return;
        AuthorResolver.AssignAuthors(work, authors);

        var parsed = PartialDateParser.TryParse(FirstPublishedDate) ?? PartialDate.Empty;
        work.FirstPublishedDate = parsed.Date;
        work.FirstPublishedDatePrecision = parsed.Precision;

        work.SeriesId = SelectedSeriesId;
        work.SeriesOrder = SelectedSeriesId.HasValue ? SeriesOrder : null;

        // Reconcile Genres to match the selection. Load requested genres
        // by id and replace the work's collection.
        var desired = await db.Genres.Where(g => SelectedGenreIds.Contains(g.Id)).ToListAsync();
        work.Genres.Clear();
        foreach (var g in desired) work.Genres.Add(g);

        await db.SaveChangesAsync();
    }

    public record SeriesOption(int Id, string Name, SeriesType Type);
}
