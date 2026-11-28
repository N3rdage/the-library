namespace BookTracker.Data.Models;

// Whether a Copy is still in the collection and, if not, why it left. Stored
// as int; Owned is 0 so copies that predate the column read as owned. Written
// only through Copy.RemoveFromCollection / Copy.RestoreToCollection.
public enum CopyDisposition
{
    Owned = 0,
    Donated = 1,
    Lost = 2,
    Damaged = 3,
    Sold = 4,
}
