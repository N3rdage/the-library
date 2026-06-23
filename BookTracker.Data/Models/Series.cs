using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

public enum SeriesType
{
    /// <summary>Numbered series with a known order (e.g. The Ender's Game Saga).</summary>
    Series,

    /// <summary>Loose collection without strict ordering (e.g. Discworld, Hercule Poirot).</summary>
    Collection
}

public class Series
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Author { get; set; }

    public SeriesType Type { get; set; } = SeriesType.Series;

    /// <summary>Expected number of works in a Series. Null for Collections or unknown.</summary>
    public int? ExpectedCount { get; set; }

    public string? Description { get; set; }

    // Series membership is a per-Work concept after the Work refactor.
    // A Christie short-story collection (Book) doesn't belong to the
    // Poirot series — its constituent Stories (Works) do.
    public List<Work> Works { get; set; } = [];

    // --- Aggregate behaviour -------------------------------------------------
    // Series is a thin aggregate: no shared m2m, no ref-count lifecycle (Work
    // owns the Work↔Series link via Work.AssignToSeries/ClearSeries). Its one
    // invariant is the ExpectedCount/Type pairing below — a target count only
    // means anything for an ordered Series, never a loose Collection. Cross-row
    // rules the entity can't see (name uniqueness) live in the handlers.
    // Deletion is FK-driven (Work.SeriesId is ON DELETE SET NULL), so there's no
    // delete method here. See docs/BACKEND-REFACTOR-DESIGN.md.

    /// <summary>Creates a Series with its details applied (and the
    /// ExpectedCount/Type invariant enforced). Name is required.</summary>
    public static Series Create(string name, string? author, SeriesType type, int? expectedCount, string? description)
    {
        var series = new Series();
        series.UpdateDetails(name, author, type, expectedCount, description);
        return series;
    }

    /// <summary>Applies the editable details. A target <paramref name="expectedCount"/>
    /// is only retained for an ordered <see cref="SeriesType.Series"/> — a
    /// Collection has no meaningful count, so it's nulled regardless of input.</summary>
    public void UpdateDetails(string name, string? author, SeriesType type, int? expectedCount, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainRuleException("Series name is required.");
        Name = name.Trim();
        Author = author.TrimToNull();
        Type = type;
        ExpectedCount = type == SeriesType.Series ? expectedCount : null;
        Description = description.TrimToNull();
    }
}
