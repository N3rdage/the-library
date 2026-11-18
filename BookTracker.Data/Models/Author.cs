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
    public string Name { get; private set; } = string.Empty;

    /// <summary>Null for canonical authors. Set for pen-name / alias rows pointing at their canonical entry.</summary>
    public int? CanonicalAuthorId { get; private set; }
    public Author? CanonicalAuthor { get; set; }

    /// <summary>Inverse of CanonicalAuthorId — pen names that resolve to this Author.</summary>
    public List<Author> Aliases { get; set; } = [];

    /// <summary>Skip-navigation through WorkAuthor (M:N). Includes works where this Author is a co-author, not just the lead.</summary>
    public List<Work> Works { get; set; } = [];

    /// <summary>Explicit join collection — useful when the Order field matters or when iterating join rows directly.</summary>
    public List<WorkAuthor> WorkAuthors { get; set; } = [];

    // --- Aggregate behaviour -------------------------------------------------
    // The entity owns its name rule (required, trimmed) and its alias pointer.
    // Cross-row rules it can't see stay in the handlers: name uniqueness
    // (RenameAuthor's clash check, AuthorResolver's find-or-create) and the
    // shape of the alias graph (MarkAuthorAsAliasOf re-roots onto the target's
    // canonical and re-points this row's prior aliases; MergeAuthors re-homes
    // the loser's aliases). See docs/BACKEND-REFACTOR-DESIGN.md.

    /// <summary>Creates a canonical Author with the trimmed name. Name is required.</summary>
    public static Author Create(string name)
    {
        var author = new Author();
        author.Rename(name);
        return author;
    }

    /// <summary>Sets the trimmed name. Name is required; uniqueness is the caller's check.</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainRuleException("Author name is required.");
        Name = name.Trim();
    }

    /// <summary>Points this Author at <paramref name="canonical"/> as a pen-name
    /// alias. Sets the navigation as well as the key so an unsaved canonical is
    /// linked when the graph is inserted. Takes the target as given: re-rooting
    /// onto the target's own canonical is the caller's job, because a merge
    /// re-points aliases at the winner without re-rooting.</summary>
    public void MarkAliasOf(Author canonical)
    {
        CanonicalAuthor = canonical;
        CanonicalAuthorId = canonical.Id;
    }

    /// <summary>Makes this Author canonical (standalone) by clearing its alias pointer.</summary>
    public void PromoteToCanonical()
    {
        CanonicalAuthor = null;
        CanonicalAuthorId = null;
    }
}
