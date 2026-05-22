namespace BookTracker.Data.Models;

/// <summary>
/// What kind of contribution this person made to the Work. Default is
/// <see cref="Author"/> — the original-content author. Other roles cover
/// the contributor types that surface in non-fiction (editors of reference
/// works, translators of classical / sacred texts, illustrators of
/// graphic novels and children's books).
///
/// Rollup queries that count "books by X" default to <c>Role = Author</c>
/// to protect the Top-Authors report from being polluted by translators
/// and editors of reference works. Anything wider is an explicit opt-in.
/// </summary>
public enum AuthorRole
{
    Author = 0,        // original-content author (default)
    Editor = 1,
    Translator = 2,
    Illustrator = 3,
    Adaptor = 4,
    Compiler = 5,
    Foreword = 6,      // prominent forewords (Bryson writing a foreword to X)
    Contributor = 7,   // multi-contributor anthologies where the named individual is one of many
}

// Join entity for Work ↔ Author (M:N). A Work can credit multiple Authors
// (Preston + Child writing *Relic* together; the Destroyer series credits
// Murphy + Sapir for ~90% of the run).
//
// `Order` = display position. 0 = lead/primary, ascending for additional
// authors — keeps "Preston & Child" stable rather than alphabetising.
// Order is per-Role: a Work's Authors share an Order sequence, and its
// Translators share a separate Order sequence.
//
// `Role` = what kind of contribution this is. Default Author for original
// content; other roles for editors / translators / illustrators / etc.
// See [[AuthorRole]] for the enum.
//
// Author canonical-alias rollup chains transparently: each WorkAuthor.Author
// → Author.CanonicalAuthorId resolves the same way as before. A Work
// co-credited to Stephen King + Richard Bachman rolls up under King's
// canonical for aggregations that dedupe by canonical.
//
// Composite PK (WorkId, AuthorId, Role) encodes the invariant that the
// same Author can't appear twice on a single Work in the SAME role —
// but the same Author CAN hold multiple roles on the same Work
// (Tolkien is Author + Illustrator on *The Hobbit*).
public class WorkAuthor
{
    public int WorkId { get; set; }
    public Work Work { get; set; } = null!;

    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;

    /// <summary>Display position. 0 = lead/primary author, ascending for additional authors. Per-Role.</summary>
    public int Order { get; set; }

    /// <summary>
    /// What kind of contribution this person made to the Work. Default
    /// <see cref="AuthorRole.Author"/>; rollup queries filter to Author
    /// by default and opt in to other roles explicitly.
    /// </summary>
    public AuthorRole Role { get; set; }
}
