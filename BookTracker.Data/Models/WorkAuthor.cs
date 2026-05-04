namespace BookTracker.Data.Models;

// Join entity for Work ↔ Author (M:N). A Work can credit multiple Authors
// (Preston + Child writing *Relic* together; the Destroyer series credits
// Murphy + Sapir for ~90% of the run).
//
// `Order` = display position. 0 = lead/primary, ascending for additional
// authors — keeps "Preston & Child" stable rather than alphabetising.
//
// Author canonical-alias rollup chains transparently: each WorkAuthor.Author
// → Author.CanonicalAuthorId resolves the same way as before. A Work
// co-credited to Stephen King + Richard Bachman rolls up under King's
// canonical for aggregations that dedupe by canonical.
//
// Composite PK (WorkId, AuthorId) encodes the invariant that the same
// Author can't appear twice on a single Work.
//
// PR1 of the multi-author cutover (additive): Work keeps both AuthorId
// (legacy single-author FK) and WorkAuthors (this join) — saves dual-write,
// reads still come from AuthorId. PR2 drops AuthorId and switches reads
// to WorkAuthors.
public class WorkAuthor
{
    public int WorkId { get; set; }
    public Work Work { get; set; } = null!;

    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;

    /// <summary>Display position. 0 = lead/primary author, ascending for additional authors.</summary>
    public int Order { get; set; }
}
