using BookTracker.Web.Services;

namespace BookTracker.Web.ViewModels;

// Backs /duplicates/merge/author/{idA}/{idB}. Loads both Author details with
// work/alias counts + sample titles for the review cards, then calls
// AuthorMergeService.MergeAsync when the user confirms a winner. Errors
// (incompatible aliases, missing entities) flow back through ErrorMessage.
public class AuthorMergeViewModel(IAuthorMergeService merger)
{
    public bool Loading { get; private set; } = true;
    public bool Merging { get; private set; }

    public AuthorMergeDetail? Lower { get; private set; }
    public AuthorMergeDetail? Higher { get; private set; }

    public string? IncompatibilityReason { get; private set; }
    public string? ErrorMessage { get; private set; }

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
        ErrorMessage = null;
        var result = await merger.LoadAsync(idA, idB);
        Lower = result.Lower;
        Higher = result.Higher;
        IncompatibilityReason = result.IncompatibilityReason;
        if (Lower is null || Higher is null)
        {
            ErrorMessage = "One or both authors could not be loaded — they may have been merged or deleted already.";
        }
        Loading = false;
    }

    public async Task<AuthorMergeResult?> MergeAsync()
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
