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
}
