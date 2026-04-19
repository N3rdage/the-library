using System.Text.RegularExpressions;
using BookTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class GenrePickerViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public List<GenreNode> TopLevelGenres { get; private set; } = [];
    public Dictionary<int, GenreNode> GenreById { get; private set; } = [];
    public List<int> SelectedGenreIds { get; set; } = [];
    public List<string> LookupCandidates { get; set; } = [];

    public async Task InitializeAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new GenreNode(g.Id, g.Name, g.ParentGenreId))
            .ToListAsync();
        GenreById = all.ToDictionary(g => g.Id);
        foreach (var g in all.Where(g => g.ParentGenreId.HasValue))
        {
            if (GenreById.TryGetValue(g.ParentGenreId!.Value, out var parent))
            {
                parent.Children.Add(g);
            }
        }
        TopLevelGenres = all.Where(g => g.ParentGenreId is null).OrderBy(g => g.Name).ToList();
    }

    public void ToggleGenre(int id, bool isChecked)
    {
        if (isChecked)
        {
            if (!SelectedGenreIds.Contains(id)) SelectedGenreIds.Add(id);
            if (GenreById.TryGetValue(id, out var node) && node.ParentGenreId is int parentId
                && !SelectedGenreIds.Contains(parentId))
            {
                SelectedGenreIds.Add(parentId);
            }
        }
        else
        {
            SelectedGenreIds.Remove(id);
        }
    }

    public void ApplyLookupCandidates(IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var match = GenreById.Values.FirstOrDefault(g => FuzzyGenreMatch(candidate, g.Name));
            if (match is not null)
            {
                ToggleGenre(match.Id, true);
            }
        }
    }

    // Match a free-form upstream subject string ("Detective and mystery
    // stories, English") against one of our preset genre names ("Mystery").
    // The preset must appear inside the candidate as a contiguous, word-
    // bounded phrase. The previous looser variant matched in either
    // direction on a letters-only blob, which inflated short subjects like
    // "Science" into "Science Fiction" matches.
    public static bool FuzzyGenreMatch(string candidate, string preset)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(preset)) return false;

        var c = candidate.Trim().ToLowerInvariant();
        var p = preset.Trim().ToLowerInvariant();

        if (c == p) return true;

        // \b matches a transition between word/non-word characters, so
        // multi-word presets ("Science Fiction") only fire when the whole
        // phrase appears in the candidate.
        var pattern = $@"\b{Regex.Escape(p)}\b";
        return Regex.IsMatch(c, pattern);
    }

    // Retained for any external callers that build their own keys.
    public static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    public class GenreNode(int id, string name, int? parentGenreId)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public int? ParentGenreId { get; } = parentGenreId;
        public List<GenreNode> Children { get; } = [];
    }
}
