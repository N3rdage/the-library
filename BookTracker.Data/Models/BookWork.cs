namespace BookTracker.Data.Models;

// Join entity for Book ↔ Work (M:N). A Book can contain multiple Works (a
// short-story collection / anthology) and a Work can appear in multiple Books
// (a Christie story reprinted across compendiums).
//
// `Order` = the Work's display position WITHIN this Book (0-based). Because a
// Work lives in several Books, the ordering is a per-Book concern and so has to
// live on the join, not on Work. New Works append to the end (max + 1); the
// Book Detail page shows them in this order and lets the user reorder (TODO
// #57). Ties (all-0) only arise for rows created straight through the
// Book.Works skip-navigation, which doesn't carry the payload — every ordered
// append routes through Book.AttachWork so the sequence stays meaningful.
//
// The FK COLUMNS keep the skip-nav convention names (BooksId / WorksId) they
// were born with as an implicit join, so promoting to this explicit entity is a
// pure add-a-column migration with no data-moving rename. The C# property names
// are the cleaner BookId / WorkId.
//
// Composite PK (BookId, WorkId): a Work can't appear twice in the same Book.
public class BookWork
{
    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    public int WorkId { get; set; }
    public Work Work { get; set; } = null!;

    /// <summary>The Work's 0-based display position within this Book. New Works append to the end.</summary>
    public int Order { get; set; }
}
