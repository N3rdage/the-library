using BookTracker.Web.Services;

namespace BookTracker.Web.ViewModels;

// Backs /duplicates/merge/edition/{idA}/{idB}. Same shape as
// WorkMergeViewModel. Auto-fill-empties semantics on merge; enrichment
// preview recomputes on winner selection.
public class EditionMergeViewModel(IEditionMergeService merger)
{
    public bool Loading { get; private set; } = true;
    public bool Merging { get; private set; }

    public EditionMergeDetail? Lower { get; private set; }
    public EditionMergeDetail? Higher { get; private set; }

    public string? IncompatibilityReason { get; private set; }
    public string? ErrorMessage { get; private set; }

    public int? SelectedWinnerId { get; set; }

    public int? LoserId =>
        SelectedWinnerId is null ? null
        : SelectedWinnerId == Lower?.Id ? Higher?.Id
        : SelectedWinnerId == Higher?.Id ? Lower?.Id
        : null;

    public EditionMergeDetail? Loser =>
        LoserId is null ? null
        : LoserId == Lower?.Id ? Lower
        : LoserId == Higher?.Id ? Higher
        : null;

    public bool CanMerge =>
        !Loading && !Merging
        && IncompatibilityReason is null
        && Lower is not null && Higher is not null
        && SelectedWinnerId is not null;

    public IReadOnlyList<string> EnrichmentHints
    {
        get
        {
            if (Loser is null || SelectedWinnerId is null) return [];
            var winner = SelectedWinnerId == Lower?.Id ? Lower : Higher;
            if (winner is null) return [];
            return ComputeEnrichmentHints(winner, Loser);
        }
    }

    private static IReadOnlyList<string> ComputeEnrichmentHints(EditionMergeDetail winner, EditionMergeDetail loser)
    {
        var hints = new List<string>();
        if (winner.DatePrinted is null && loser.DatePrinted is not null)
        {
            hints.Add($"Date printed {loser.DatePrinted:d MMM yyyy}");
        }
        if (string.IsNullOrWhiteSpace(winner.CoverArtUrl) && !string.IsNullOrWhiteSpace(loser.CoverArtUrl))
        {
            hints.Add("Cover image");
        }
        if (string.IsNullOrEmpty(winner.PublisherName) && !string.IsNullOrEmpty(loser.PublisherName))
        {
            hints.Add($"Publisher \"{loser.PublisherName}\"");
        }
        if (string.IsNullOrWhiteSpace(winner.Isbn) && !string.IsNullOrWhiteSpace(loser.Isbn))
        {
            hints.Add($"ISBN {loser.Isbn}");
        }
        return hints;
    }

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
            ErrorMessage = "One or both Editions could not be loaded — they may have been merged or deleted already.";
        }
        Loading = false;
    }

    public async Task<EditionMergeResult?> MergeAsync()
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
