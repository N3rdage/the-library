using System.Text.RegularExpressions;
using BookTracker.Data;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

namespace BookTracker.Web.ViewModels;

// Backing VM for the MudBlazor genre picker used in WorkEditDialog.
// The component has three surfaces (chips, typeahead, collapsible tree)
// that all manipulate the same SelectedGenreIds set — this VM owns the
// full genre catalog and the search/projection helpers; selection
// state is owned by the component (passed in as a parameter, kept
// in sync via a callback).
public class MudGenrePickerViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public List<GenreRow> AllGenres { get; private set; } = [];
    private Dictionary<int, GenreRow> _byId = [];

    public async Task InitializeAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.ParentGenreId })
            .ToListAsync();

        var byId = rows.ToDictionary(r => r.Id, r => r);

        AllGenres = rows.Select(r => new GenreRow(
            r.Id,
            r.Name,
            r.ParentGenreId,
            r.ParentGenreId is int pid && byId.TryGetValue(pid, out var parent) ? parent.Name : null))
            .ToList();

        _byId = AllGenres.ToDictionary(g => g.Id);
    }

    /// <summary>Typeahead — substring match over the flat list, case-insensitive, excludes already-selected.</summary>
    public IEnumerable<GenreRow> Search(string? query, IReadOnlyCollection<int> alreadySelected)
    {
        var q = (query ?? "").Trim();
        var skip = alreadySelected.ToHashSet();
        IEnumerable<GenreRow> matches = AllGenres.Where(g => !skip.Contains(g.Id));
        if (!string.IsNullOrEmpty(q))
        {
            matches = matches.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return matches.Take(20);
    }

    /// <summary>The chip label for a genre — "Parent / Leaf" when nested, just "Leaf" for top-level.</summary>
    public string ChipLabel(int genreId) =>
        _byId.TryGetValue(genreId, out var g) && g.ParentName is string parent
            ? $"{parent} / {g.Name}"
            : _byId.TryGetValue(genreId, out var flat) ? flat.Name : "(unknown)";

    /// <summary>The dropdown label used by the autocomplete — "Parent > Child" when nested, just leaf otherwise.</summary>
    public string DropdownLabel(GenreRow g) =>
        g.ParentName is string parent ? $"{parent} > {g.Name}" : g.Name;

    /// <summary>MudTreeView items — top-level parents with children attached.</summary>
    public List<TreeItemData<int>> BuildTreeItems(IReadOnlyCollection<int> selectedIds)
    {
        var selected = selectedIds.ToHashSet();
        var topLevel = AllGenres.Where(g => g.ParentGenreId is null).OrderBy(g => g.Name).ToList();
        return topLevel.Select(top => new TreeItemData<int>
        {
            Value = top.Id,
            Text = top.Name,
            Expandable = AllGenres.Any(g => g.ParentGenreId == top.Id),
            Children = AllGenres.Where(g => g.ParentGenreId == top.Id)
                .OrderBy(g => g.Name)
                .Select(child => new TreeItemData<int>
                {
                    Value = child.Id,
                    Text = child.Name,
                    Expandable = false,
                })
                .ToList<TreeItemData<int>>(),
        }).ToList();
    }

    /// <summary>Adds a genre to the selection set. Auto-selects the parent when a child is picked — matches the
    /// existing Bootstrap picker's behaviour so a "Dictionaries" selection implies "Reference" too.</summary>
    public List<int> AddGenre(int genreId, IReadOnlyCollection<int> current)
    {
        var result = current.ToList();
        if (!result.Contains(genreId)) result.Add(genreId);
        if (_byId.TryGetValue(genreId, out var g) && g.ParentGenreId is int parentId && !result.Contains(parentId))
        {
            result.Add(parentId);
        }
        return result;
    }

    public List<int> RemoveGenre(int genreId, IReadOnlyCollection<int> current)
    {
        return current.Where(id => id != genreId).ToList();
    }

    /// <summary>Walks the ISBN-lookup genre candidates (e.g. Open Library
    /// subjects like "Detective and mystery stories, English"), fuzzy-
    /// matches each against the preset genre catalogue, and returns the
    /// updated selection list with matches added. Idempotent — already-
    /// selected matches stay; user can manually remove a fuzzy pick after
    /// the apply and it won't re-add on subsequent renders because the
    /// component tracks the last-applied candidate list.</summary>
    public List<int> ApplyLookupCandidates(IReadOnlyList<string> candidates, IReadOnlyCollection<int> current)
    {
        var next = current.ToList();
        foreach (var candidate in candidates)
        {
            var match = AllGenres.FirstOrDefault(g => FuzzyGenreMatch(candidate, g.Name));
            if (match is not null)
            {
                next = AddGenre(match.Id, next);
            }
        }
        return next;
    }

    /// <summary>Match a free-form upstream subject string ("Detective and
    /// mystery stories, English") against one of our preset genre names
    /// ("Mystery"). The preset must appear inside the candidate as a
    /// contiguous, word-bounded phrase. Word-boundary regex protects
    /// against substring-only matches ("Science" → "Science Fiction" or
    /// "Romance" → "Romanticism").
    /// Static so non-picker callers (BulkAddViewModel's per-row genre
    /// hint) can reuse the same matcher.</summary>
    public static bool FuzzyGenreMatch(string candidate, string preset)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(preset)) return false;

        var c = candidate.Trim().ToLowerInvariant();
        var p = preset.Trim().ToLowerInvariant();

        if (c == p) return true;

        // \b matches a transition between word / non-word characters, so
        // multi-word presets ("Science Fiction") only fire when the whole
        // phrase appears in the candidate.
        var pattern = $@"\b{Regex.Escape(p)}\b";
        return Regex.IsMatch(c, pattern);
    }

    public record GenreRow(int Id, string Name, int? ParentGenreId, string? ParentName);
}
