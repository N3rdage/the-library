using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// A Work is the abstract creative unit — a story, novel, play, or poem.
// Multiple Books can contain the same Work (a Christie short story
// reprinted across several compendiums), and a single Book can contain
// multiple Works (a short-story collection).
//
// PR 1 of the Work refactor introduces this entity and dual-writes it
// alongside the legacy Book.{Subtitle,Author,Genres,SeriesId,...} fields.
// PR 2 will cut over reads + drop the legacy columns. Until PR 2 ships,
// every existing Book has exactly one mirroring Work; the m:m schema is
// here to make the eventual N:N transition free.
public class Work
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Subtitle { get; set; }

    [Required, MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    /// <summary>The year/date the Work was first published — distinct from any specific Edition's print date.</summary>
    public DateOnly? FirstPublishedDate { get; set; }

    public List<Genre> Genres { get; set; } = [];

    public int? SeriesId { get; set; }
    public Series? Series { get; set; }

    /// <summary>Position in a Series (1-based). Defaults to publication order for Collections.</summary>
    public int? SeriesOrder { get; set; }

    public List<Book> Books { get; set; } = [];
}
