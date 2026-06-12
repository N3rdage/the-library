using BookTracker.Web.Services;

namespace BookTracker.Web.ViewModels;

// Backs /duplicates/merge/work/{idA}/{idB}. Mirrors AuthorMergeViewModel
// shape — load preview, user picks winner, commit via service. Merge is
// auto-fill-empties: any winner field that's empty gets taken from the
// loser; winner fields that are already set are preserved. Genres union.
// The preview surfaces which fields will be auto-filled so the user can
// override (by editing the winner) before confirming.
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

    // Human-readable list of what auto-fill will pull from loser → winner
    // based on the current winner selection. Recomputed live so the UI can
    // re-render on radio change without a server round-trip.
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

    private static IReadOnlyList<string> ComputeEnrichmentHints(WorkMergeDetail winner, WorkMergeDetail loser)
    {
        var hints = new List<string>();
        if (string.IsNullOrWhiteSpace(winner.Subtitle) && !string.IsNullOrWhiteSpace(loser.Subtitle))
        {
            hints.Add($"Subtitle \"{loser.Subtitle}\"");
        }
        if (!winner.FirstPublishedYear.HasValue && loser.FirstPublishedYear.HasValue)
        {
            hints.Add($"First-published year {loser.FirstPublishedYear}");
        }
        if (string.IsNullOrEmpty(winner.SeriesName) && !string.IsNullOrEmpty(loser.SeriesName))
        {
            hints.Add($"Series \"{loser.SeriesName}\"" + (!string.IsNullOrEmpty(loser.SeriesOrderLabel) ? $" (#{loser.SeriesOrderLabel})" : ""));
        }
        var winnerGenres = winner.GenreNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedGenres = loser.GenreNames.Where(g => !winnerGenres.Contains(g)).ToList();
        if (addedGenres.Count > 0)
        {
            hints.Add($"{addedGenres.Count} genre{(addedGenres.Count == 1 ? "" : "s")}: {string.Join(", ", addedGenres)}");
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
