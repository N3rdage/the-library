using BookTracker.Application;
using BookTracker.Application.Duplicates;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

// Backs the /duplicates page. Holds the most recent detection report and
// exposes Dismiss / Un-ignore commands; the page reloads the report after
// each mutation (recompute-on-demand, no caching by design).
public class DuplicatesViewModel(IDispatcher dispatcher)
{
    public bool Loading { get; private set; } = true;
    public DuplicateReport? Report { get; private set; }
    public DuplicateEntityType ActiveTab { get; set; } = DuplicateEntityType.Author;
    public string? SuccessMessage { get; set; }

    public async Task LoadAsync()
    {
        Loading = true;
        await dispatcher.Send(new SweepOrphanedDismissals());
        Report = await dispatcher.Query(new GetDuplicateReport());
        Loading = false;
    }

    public async Task DismissAsync(DuplicateEntityType type, int idA, int idB, string? note = null)
    {
        await dispatcher.Send(new DismissDuplicatePair(type, idA, idB, note));
        SuccessMessage = "Pair dismissed — you can un-ignore it from the Dismissed section.";
        await LoadAsync();
    }

    public async Task UnignoreAsync(int ignoredDuplicateId)
    {
        await dispatcher.Send(new RestoreDismissedDuplicate(ignoredDuplicateId));
        SuccessMessage = "Pair un-ignored — back in the active list.";
        await LoadAsync();
    }

    public IReadOnlyList<AuthorDuplicatePair> ActiveAuthorPairs =>
        Report?.Authors.Where(p => p.Dismissed is null).ToList() ?? [];
    public IReadOnlyList<AuthorDuplicatePair> DismissedAuthorPairs =>
        Report?.Authors.Where(p => p.Dismissed is not null).ToList() ?? [];

    public IReadOnlyList<WorkDuplicatePair> ActiveWorkPairs =>
        Report?.Works.Where(p => p.Dismissed is null).ToList() ?? [];
    public IReadOnlyList<WorkDuplicatePair> DismissedWorkPairs =>
        Report?.Works.Where(p => p.Dismissed is not null).ToList() ?? [];

    public IReadOnlyList<BookDuplicatePair> ActiveBookPairs =>
        Report?.Books.Where(p => p.Dismissed is null).ToList() ?? [];
    public IReadOnlyList<BookDuplicatePair> DismissedBookPairs =>
        Report?.Books.Where(p => p.Dismissed is not null).ToList() ?? [];

    public IReadOnlyList<EditionDuplicatePair> ActiveEditionPairs =>
        Report?.Editions.Where(p => p.Dismissed is null).ToList() ?? [];
    public IReadOnlyList<EditionDuplicatePair> DismissedEditionPairs =>
        Report?.Editions.Where(p => p.Dismissed is not null).ToList() ?? [];

    public int ActiveCount(DuplicateEntityType t) => t switch
    {
        DuplicateEntityType.Author => ActiveAuthorPairs.Count,
        DuplicateEntityType.Work => ActiveWorkPairs.Count,
        DuplicateEntityType.Book => ActiveBookPairs.Count,
        DuplicateEntityType.Edition => ActiveEditionPairs.Count,
        _ => 0
    };
}
