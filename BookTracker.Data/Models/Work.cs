using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// A Work is the abstract creative unit — a story, novel, play, or poem.
// Multiple Books can contain the same Work (a Christie short story
// reprinted across several compendiums), and a single Book can contain
// multiple Works (a short-story collection).
//
// Authorship is many-to-many via the WorkAuthor join entity (PR2 of
// the multi-author cutover). Each WorkAuthor row points at the SPECIFIC
// Author entity used (Stephen King vs. the Richard Bachman alias) so the
// book is shown as actually published; aggregations roll aliases up via
// Author.CanonicalAuthorId. WorkAuthor.Order keeps the lead author first
// on display ("Preston & Child" stays in that order rather than being
// alphabetised).
public class Work
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Subtitle { get; set; }

    /// <summary>Explicit join with Order. The canonical read source for ordered display ("Preston & Child").</summary>
    public List<WorkAuthor> WorkAuthors { get; set; } = [];

    /// <summary>Skip-navigation through WorkAuthor — convenient for "any author of this work" semantics; does NOT preserve Order.</summary>
    public List<Author> Authors { get; set; } = [];

    /// <summary>The year/date the Work was first published — distinct from any specific Edition's print date.</summary>
    public DateOnly? FirstPublishedDate { get; set; }

    /// <summary>How precise <see cref="FirstPublishedDate"/> is — drives display formatting.</summary>
    public DatePrecision FirstPublishedDatePrecision { get; set; } = DatePrecision.Day;

    public List<Genre> Genres { get; set; } = [];

    public int? SeriesId { get; set; }
    public Series? Series { get; set; }

    /// <summary>Position in a Series (1-based). Defaults to publication order for Collections.</summary>
    public int? SeriesOrder { get; set; }

    public List<Book> Books { get; set; } = [];
}
