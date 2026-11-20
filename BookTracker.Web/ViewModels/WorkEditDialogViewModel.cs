using BookTracker.Application;
using BookTracker.Application.Works;
using BookTracker.Web.Services;
using BookTracker.Application.Formatting;

namespace BookTracker.Web.ViewModels;

// Dialog-scoped VM for "Edit work" on the View page. Edits a single
// Work's title, subtitle, author (find-or-create with typeahead), first
// published date (PartialDateParser), and genres (via MudGenrePicker — PR B).
// Series membership is a per-Book concept now — edited via BookEditDialog.
public class WorkEditDialogViewModel(IDispatcher dispatcher) : DispatchingViewModel
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

        var work = await dispatcher.Query(new GetWorkForEdit(workId));
        if (work is null) { NotFound = true; return; }

        Title = work.Title;
        Subtitle = work.Subtitle;
        // The read returns the lead author first and contributors by role, then
        // order. Every Work has at least one WorkAuthor (Role=Author)
        // post-Phase-A default-value migration.
        AuthorNames = [.. work.AuthorNames];
        Contributors = work.Contributors
            .Select(c => new ContributorEntry(c.Name, c.Role))
            .ToList();
        FirstPublishedDate = PartialDateParser.Format(work.FirstPublishedDate, work.FirstPublishedDatePrecision);
        SelectedGenreIds = [.. work.GenreIds];
    }

    /// <summary>True when saved; false when the dialog state is stale or the
    /// domain refused (see <see cref="DispatchingViewModel.Error"/>).</summary>
    public async Task<bool> SaveAsync()
    {
        if (NotFound || string.IsNullOrWhiteSpace(Title) || AuthorNames.All(string.IsNullOrWhiteSpace)) return false;

        var parsed = PartialDateParser.TryParse(FirstPublishedDate) ?? PartialDate.Empty;

        var contributorInputs = Contributors
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new ContributorInput(c.Name, c.Role))
            .ToList();

        return await DispatchAsync(() => dispatcher.Send(new UpdateWork(
            WorkId, Title, Subtitle, AuthorNames, contributorInputs,
            parsed.Date, parsed.Precision, SelectedGenreIds)));
    }
}
