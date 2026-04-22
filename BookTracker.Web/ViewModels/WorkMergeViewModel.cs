using BookTracker.Web.Services;

namespace BookTracker.Web.ViewModels;

// Backs /duplicates/merge/work/{idA}/{idB}. Mirrors AuthorMergeViewModel
// shape — load preview, user picks winner, commit via service. No
// auto-enrichment on merge: if user wants subtitle / series / genres
// from the loser they copy manually before confirming.
public class WorkMergeViewModel(IWorkMergeService merger)
{
    public bool Loading { get; private set; } = true;
    public bool Merging { get; private set; }

    public WorkMergeDetail? Lower { get; private set; }
    public WorkMergeDetail? Higher { get; private set; }

    public string? IncompatibilityReason { get; private set; }
    public string? ErrorMessage { get; private set; }

    public int SharedBookCount { get; private set; }

    public int? SelectedWinnerId { get; set; }

    public int? LoserId =>
        SelectedWinnerId is null ? null
        : SelectedWinnerId == Lower?.Id ? Higher?.Id
        : SelectedWinnerId == Higher?.Id ? Lower?.Id
        : null;

    public WorkMergeDetail? Loser =>
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
        ErrorMessage = null;
        var result = await merger.LoadAsync(idA, idB);
        Lower = result.Lower;
        Higher = result.Higher;
        IncompatibilityReason = result.IncompatibilityReason;
        SharedBookCount = result.SharedBookCount;
        if (Lower is null || Higher is null)
        {
            ErrorMessage = "One or both Works could not be loaded — they may have been merged or deleted already.";
        }
        Loading = false;
    }

    public async Task<WorkMergeResult?> MergeAsync()
    {
        if (!CanMerge || SelectedWinnerId is null || LoserId is null) return null;

        Merging = true;
        try
        {
            var result = await merger.MergeAsync(SelectedWinnerId.Value, LoserId.Value);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
            return result;
        }
        finally
        {
            Merging = false;
        }
    }
}
