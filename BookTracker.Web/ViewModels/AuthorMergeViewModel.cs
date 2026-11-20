using BookTracker.Application;
using BookTracker.Application.Authors;

namespace BookTracker.Web.ViewModels;

// Backs /duplicates/merge/author/{idA}/{idB}. Loads both Author details with
// work/alias counts + sample titles for the review cards, then dispatches the
// MergeAuthors command when the user confirms a winner. Errors (incompatible
// aliases, missing entities) flow back through Error. Dispatcher-only:
// reads via GetAuthorMergePreview, writes via MergeAuthors.
public class AuthorMergeViewModel(IDispatcher dispatcher) : DispatchingViewModel
{
    public bool Loading { get; private set; } = true;
    public bool Merging { get; private set; }

    public AuthorMergeDetail? Lower { get; private set; }
    public AuthorMergeDetail? Higher { get; private set; }

    public string? IncompatibilityReason { get; private set; }

    public int? SelectedWinnerId { get; set; }

    public int? LoserId =>
        SelectedWinnerId is null ? null
        : SelectedWinnerId == Lower?.Id ? Higher?.Id
        : SelectedWinnerId == Higher?.Id ? Lower?.Id
        : null;

    public AuthorMergeDetail? Loser =>
        LoserId is null ? null
        : LoserId == Lower?.Id ? Lower
        : LoserId == Higher?.Id ? Higher
        : null;

    public bool CanMerge =>
        !Loading && !Merging
        && IncompatibilityReason is null
        && Lower is not null && Higher is not null
        && SelectedWinnerId is not null;

    public async Task LoadAsync(int idA, int idB)
    {
        Loading = true;
        Error = null;
        var result = await dispatcher.Query(new GetAuthorMergePreview(idA, idB));
        Lower = result.Lower;
        Higher = result.Higher;
        IncompatibilityReason = result.IncompatibilityReason;
        if (Lower is null || Higher is null)
        {
            Error = "One or both authors could not be loaded — they may have been merged or deleted already.";
        }
        Loading = false;
    }

    public async Task<AuthorMergeResult?> MergeAsync()
    {
        if (!CanMerge || SelectedWinnerId is null || LoserId is null) return null;

        Merging = true;
        try
        {
            var result = await DispatchAsync(() => dispatcher.Send(new MergeAuthors(SelectedWinnerId.Value, LoserId.Value)));
            if (result is { Success: false }) Error = result.ErrorMessage;
            return result;
        }
        finally
        {
            Merging = false;
        }
    }
}
