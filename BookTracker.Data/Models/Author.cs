using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// Self-referential model for pen names. A canonical Author has
// CanonicalAuthorId == null. An alias has CanonicalAuthorId pointing at
// its canonical Author. Stephen King is canonical; Richard Bachman is
// an alias whose CanonicalAuthorId references Stephen King.
//
// A Work always points to the SPECIFIC Author entity used (a Bachman
// novel's Work.Author is Bachman, not King) so the book is displayed
// as it was actually published. Aggregations like "top authors" group
// by `CanonicalAuthorId ?? Id` to roll all Bachman titles up under
// King's tally.
public class Author
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Null for canonical authors. Set for pen-name / alias rows pointing at their canonical entry.</summary>
    public int? CanonicalAuthorId { get; set; }
    public Author? CanonicalAuthor { get; set; }

    /// <summary>Inverse of CanonicalAuthorId — pen names that resolve to this Author.</summary>
    public List<Author> Aliases { get; set; } = [];

    /// <summary>Skip-navigation through WorkAuthor (M:N). Includes works where this Author is a co-author, not just the lead.</summary>
    public List<Work> Works { get; set; } = [];

    /// <summary>Explicit join collection — useful when the Order field matters or when iterating join rows directly.</summary>
    public List<WorkAuthor> WorkAuthors { get; set; } = [];
}
