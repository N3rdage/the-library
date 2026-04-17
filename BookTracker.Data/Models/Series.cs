using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// TODO: revisit whether Series needs multiple authors (many-to-many) for
// anthology collections — e.g. "The Best Science Fiction of the Year" has
// a different editor each volume. For now, use "Various Authors" or leave
// blank; per-book authors carry the detail.
// TODO: consider API enrichment for series detection — Open Library has
// series data for some books that could be used to auto-suggest series
// membership during ISBN lookup.

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

    /// <summary>Expected number of books in a Series. Null for Collections or unknown.</summary>
    public int? ExpectedCount { get; set; }

    public string? Description { get; set; }

    public List<Book> Books { get; set; } = [];
}
