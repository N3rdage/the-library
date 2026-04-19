using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// A Work is the abstract creative unit — a story, novel, play, or poem.
// Multiple Books can contain the same Work (a Christie short story
// reprinted across several compendiums), and a single Book can contain
// multiple Works (a short-story collection).
//
// AuthorId points at the SPECIFIC Author entity used (Stephen King vs.
// the Richard Bachman alias) so the book is shown the way it was
// actually published. Aggregations roll aliases up under their canonical
// via Author.CanonicalAuthorId.
public class Work
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Subtitle { get; set; }

    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;

    /// <summary>The year/date the Work was first published — distinct from any specific Edition's print date.</summary>
    public DateOnly? FirstPublishedDate { get; set; }

    public List<Genre> Genres { get; set; } = [];

    public int? SeriesId { get; set; }
    public Series? Series { get; set; }

    /// <summary>Position in a Series (1-based). Defaults to publication order for Collections.</summary>
    public int? SeriesOrder { get; set; }

    public List<Book> Books { get; set; } = [];
}
