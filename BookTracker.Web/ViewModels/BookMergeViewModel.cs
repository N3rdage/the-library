using BookTracker.Web.Services;

namespace BookTracker.Web.ViewModels;

// Backs /duplicates/merge/book/{idA}/{idB}. Same shape as the other merge
// VMs. Auto-fill-empties semantics plus Works/Tags union.
public class BookMergeViewModel(IBookMergeService merger)
{
    public bool Loading { get; private set; } = true;
    public bool Merging { get; private set; }

    public BookMergeDetail? Lower { get; private set; }
    public BookMergeDetail? Higher { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int? SelectedWinnerId { get; set; }

    public int? LoserId =>
        SelectedWinnerId is null ? null
        : SelectedWinnerId == Lower?.Id ? Higher?.Id
        : SelectedWinnerId == Higher?.Id ? Lower?.Id
        : null;

    public BookMergeDetail? Loser =>
        LoserId is null ? null
        : LoserId == Lower?.Id ? Lower
        : LoserId == Higher?.Id ? Higher
        : null;

    public bool CanMerge =>
        !Loading && !Merging
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

    public int WorksToUnion =>
        (Loser is null || SelectedWinnerId is null || Lower is null || Higher is null) ? 0
        : ComputeUnionCount(
            (SelectedWinnerId == Lower.Id ? Lower : Higher).WorkTitles,
            Loser.WorkTitles);

    public int TagsToUnion =>
        (Loser is null || SelectedWinnerId is null || Lower is null || Higher is null) ? 0
        : ComputeUnionCount(
            (SelectedWinnerId == Lower.Id ? Lower : Higher).TagNames,
            Loser.TagNames);

    private static int ComputeUnionCount(IReadOnlyList<string> winnerItems, IReadOnlyList<string> loserItems)
    {
        var winnerSet = winnerItems.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return loserItems.Count(t => !winnerSet.Contains(t));
    }

    private static IReadOnlyList<string> ComputeEnrichmentHints(BookMergeDetail winner, BookMergeDetail loser)
    {
        var hints = new List<string>();
        if (string.IsNullOrWhiteSpace(winner.Notes) && !string.IsNullOrWhiteSpace(loser.Notes))
        {
            // Short-preview the loser notes so the user sees what's coming.
            var preview = loser.Notes!.Length > 80 ? loser.Notes[..80] + "…" : loser.Notes;
            hints.Add($"Notes: \"{preview}\"");
        }
        if (string.IsNullOrWhiteSpace(winner.CoverArtUrl) && !string.IsNullOrWhiteSpace(loser.CoverArtUrl))
        {
            hints.Add("Cover image");
        }
        if (winner.Rating == 0 && loser.Rating > 0)
        {
            hints.Add($"Rating {loser.Rating}/5");
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
        if (Lower is null || Higher is null)
        {
            ErrorMessage = "One or both Books could not be loaded — they may have been merged or deleted already.";
        }
        Loading = false;
    }

    public async Task<BookMergeResult?> MergeAsync()
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
