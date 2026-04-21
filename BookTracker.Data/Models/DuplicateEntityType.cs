namespace BookTracker.Data.Models;

// Which entity type a duplicate-candidate pair refers to. Stored as int on
// IgnoredDuplicate so a single table covers all four entity types — the
// trade-off is no FK to the referenced entity (polymorphic); orphaned rows
// are swept up lazily by DuplicateDetectionService when the underlying
// entity has been deleted.
public enum DuplicateEntityType
{
    Author = 0,
    Work = 1,
    Book = 2,
    Edition = 3
}
