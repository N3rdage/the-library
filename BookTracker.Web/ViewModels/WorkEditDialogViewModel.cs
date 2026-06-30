using BookTracker.Application;
using BookTracker.Application.Works;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using BookTracker.Application.Formatting;

namespace BookTracker.Web.ViewModels;

// Dialog-scoped VM for "Edit work" on the View page. Edits a single
// Work's title, subtitle, author (find-or-create with typeahead), first
// published date (PartialDateParser), and genres (via MudGenrePicker — PR B).
// Series membership is a per-Book concept now — edited via BookEditDialog.
public class WorkEditDialogViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IDispatcher dispatcher)
{
    public bool NotFound { get; private set; }
    public int WorkId { get; private set; }

    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    // Multi-author chip list — populated from WorkAuthors (Role=Author only)
    // on init, replaced on save via AuthorResolver.AssignAuthors.
    public List<string> AuthorNames { get; set; } = [];
    // Non-Author contributors (Role != Author) — populated alongside
    // AuthorNames at init, written via AssignAuthors' contributors parameter.
    public List<ContributorEntry> Contributors { get; set; } = [];
    public string FirstPublishedDate { get; set; } = "";
    public List<int> SelectedGenreIds { get; set; } = [];

    public async Task InitializeAsync(int workId)
    {
        WorkId = workId;

        await using var db = await dbFactory.CreateDbContextAsync();
        var work = await db.Works
            .Include(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
            .Include(w => w.Genres)
            .FirstOrDefaultAsync(w => w.Id == workId);
        if (work is null) { NotFound = true; return; }

        Title = work.Title;
        Subtitle = work.Subtitle;
        // Order ascending so the lead author shows first in the chip list.
        // Every Work has at least one WorkAuthor (Role=Author) post-Phase-A
        // default-value migration.
        AuthorNames = work.WorkAuthors
            .Where(wa => wa.Role == AuthorRole.Author)
            .OrderBy(wa => wa.Order)
            .Select(wa => wa.Author.Name)
            .ToList();
        Contributors = work.WorkAuthors
            .Where(wa => wa.Role != AuthorRole.Author)
            .OrderBy(wa => (int)wa.Role)
            .ThenBy(wa => wa.Order)
            .Select(wa => new ContributorEntry(wa.Author.Name, wa.Role))
            .ToList();
        FirstPublishedDate = PartialDateParser.Format(work.FirstPublishedDate, work.FirstPublishedDatePrecision);
        SelectedGenreIds = work.Genres.Select(g => g.Id).ToList();
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

        var parsed = PartialDateParser.TryParse(FirstPublishedDate) ?? PartialDate.Empty;

        var contributorInputs = Contributors
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new ContributorInput(c.Name, c.Role))
            .ToList();

        try
        {
            await dispatcher.Send(new UpdateWork(
                WorkId, Title, Subtitle, AuthorNames, contributorInputs,
                parsed.Date, parsed.Precision, SelectedGenreIds));
        }
        catch (NotFoundException)
        {
            // Work deleted between opening the dialog and saving — no-op.
        }
    }
}
