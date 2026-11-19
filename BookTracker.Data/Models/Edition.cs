using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// A specific printing of a Book; part of the Book aggregate (Book → Edition →
// Copy). The scalar fields are written only through the methods below (private
// setters; EF materialises through them regardless of accessibility), so a
// handler cannot bypass an invariant and the compiler enforces it. An Edition is
// created through Book.AddEdition; the internal constructor serves that and the
// tests' seed builder (InternalsVisibleTo), and BookTracker.Application cannot
// reach it. Id stays public for EF and the tests' in-memory graphs.
public class Edition
{
    // EF materialisation.
    private Edition() { }

    /// <summary>Stores exactly the values given: nothing is trimmed or
    /// normalised here (<see cref="Book.AddEdition"/> normalises before calling),
    /// so a test seed gets exactly what it passes.</summary>
    internal Edition(
        string? isbn,
        BookFormat format,
        int? editionNumber,
        DateOnly? datePrinted,
        DatePrecision datePrintedPrecision,
        string? coverUrl,
        bool isUserSupplied,
        Publisher? publisher)
    {
        Isbn = isbn;
        Format = format;
        EditionNumber = editionNumber;
        DatePrinted = datePrinted;
        DatePrintedPrecision = datePrintedPrecision;
        CoverUrl = coverUrl;
        IsUserSupplied = isUserSupplied;
        Publisher = publisher;
    }

    public int Id { get; set; }

    public int BookId { get; private set; }

    /// <summary>The owning Book. EF fixes it up from <see cref="Book.Editions"/>
    /// and <see cref="BookId"/>; the setter is internal so the tests can attach a
    /// detached seed to its Book (InternalsVisibleTo). Application cannot set it.</summary>
    public Book Book { get; internal set; } = null!;

    // Nullable to support pre-1974 books that predate ISBN. The unique index
    // in BookTrackerDbContext is filtered (WHERE Isbn IS NOT NULL) so any
    // number of no-ISBN editions can coexist without colliding.
    [MaxLength(20)]
    public string? Isbn { get; private set; }

    public BookFormat Format { get; private set; } = BookFormat.TradePaperback;

    /// <summary>
    /// Edition revision number — 1 for first edition, 2 for the revised
    /// second, etc. Lets the catalogue distinguish e.g. *Joy of Cooking
    /// 3rd ed. (1975)* from *Joy of Cooking 8th ed. (2019)*, which are
    /// materially different content despite sharing a Work. Nullable
    /// because most editions don't surface this on the cover (mass-
    /// market fiction reprints, pre-numbered classics).
    /// </summary>
    public int? EditionNumber { get; private set; }

    public DateOnly? DatePrinted { get; private set; }

    /// <summary>How precise <see cref="DatePrinted"/> is — drives display formatting.</summary>
    public DatePrecision DatePrintedPrecision { get; private set; } = DatePrecision.Day;

    [MaxLength(500)]
    public string? CoverUrl { get; private set; }

    /// <summary>
    /// True when <see cref="CoverUrl"/> points at a user-uploaded photo
    /// (typically because no online cover was available for a rare or old
    /// edition). Mirrored covers from upstream providers stay false. Used
    /// to (a) flag the cover with a small "your photo" badge in display
    /// surfaces, and (b) skip user-supplied covers if a future re-mirror
    /// pass re-fetches upstream URLs en masse.
    /// </summary>
    public bool IsUserSupplied { get; private set; }

    public int? PublisherId { get; private set; }
    public Publisher? Publisher { get; private set; }

    /// <summary>Written only through <see cref="AddCopy"/>, <see cref="RemoveCopy"/>
    /// and <see cref="AdoptCopiesFrom"/>.</summary>
    public List<Copy> Copies { get; private set; } = [];

    /// <summary>Adds a Copy to this Edition. Returns the new Copy.</summary>
    public Copy AddCopy(BookCondition condition, DateTime? dateAcquired, string? notes)
    {
        var copy = new Copy(condition, dateAcquired, notes.TrimToNull());
        Copies.Add(copy);
        return copy;
    }

    /// <summary>Removes a Copy by id; returns true when the Edition is left
    /// with no Copies (the Book then drops the empty Edition).</summary>
    public bool RemoveCopy(int copyId)
    {
        var copy = Copies.FirstOrDefault(c => c.Id == copyId);
        if (copy is not null) Copies.Remove(copy);
        return Copies.Count == 0;
    }

    public void UpdateDetails(
        string? isbn,
        BookFormat format,
        DateOnly? datePrinted,
        DatePrecision datePrintedPrecision,
        string? coverUrl,
        Publisher? publisher)
    {
        Isbn = isbn.TrimToNull();
        Format = format;
        DatePrinted = datePrinted;
        DatePrintedPrecision = datePrintedPrecision;
        Publisher = publisher;
        PublisherId = publisher?.Id;
        CoverUrl = coverUrl.TrimToNull();
    }

    public void SetCover(string url, bool userSupplied)
    {
        CoverUrl = url;
        IsUserSupplied = userSupplied;
    }

    /// <summary>Swaps the cover URL alone — the cover mirror replacing an
    /// upstream URL with the blob URL it stored. Unlike <see cref="SetCover"/>
    /// it leaves <see cref="IsUserSupplied"/> as it is, and it stores the URL
    /// as given.</summary>
    public void ReplaceCoverUrl(string url) => CoverUrl = url;

    /// <summary>Reclassifies the binding format (the startup format backfill).</summary>
    public void ChangeFormat(BookFormat format) => Format = format;

    /// <summary>Points this Edition at publisher <paramref name="publisherId"/>
    /// by key (a publisher merge re-pointing the source's editions at the target).</summary>
    public void ReassignPublisher(int publisherId) => PublisherId = publisherId;

    /// <summary>Moves this Edition, with its Copies, to Book <paramref name="bookId"/>
    /// by key (a book merge moving the loser's editions to the winner).</summary>
    public void MoveToBook(int bookId) => BookId = bookId;

    /// <summary>Fills this Edition's empty fields from <paramref name="other"/>
    /// without overwriting any value this Edition already has: the printed date
    /// and its precision as a pair, the cover URL, the publisher (by key) and the
    /// ISBN. Returns how many of those four were filled (an edition merge, where
    /// this Edition is the winner).</summary>
    public int FillBlanksFrom(Edition other)
    {
        var filled = 0;

        if (DatePrinted is null && other.DatePrinted is not null)
        {
            DatePrinted = other.DatePrinted;
            DatePrintedPrecision = other.DatePrintedPrecision;
            filled++;
        }

        if (string.IsNullOrWhiteSpace(CoverUrl) && !string.IsNullOrWhiteSpace(other.CoverUrl))
        {
            CoverUrl = other.CoverUrl;
            filled++;
        }

        if (PublisherId is null && other.PublisherId is not null)
        {
            PublisherId = other.PublisherId;
            filled++;
        }

        if (string.IsNullOrWhiteSpace(Isbn) && !string.IsNullOrWhiteSpace(other.Isbn))
        {
            Isbn = other.Isbn;
            filled++;
        }

        return filled;
    }

    /// <summary>Re-points every Copy of <paramref name="other"/> at this Edition
    /// by key and empties <paramref name="other"/>'s Copies collection, so the
    /// emptied Edition can be deleted without EF cascading to the moved Copies
    /// (an edition merge, where this Edition is the winner). Returns how many
    /// Copies moved. Requires both Editions' Copies loaded.</summary>
    public int AdoptCopiesFrom(Edition other)
    {
        var moved = 0;
        foreach (var copy in other.Copies.ToList())
        {
            copy.MoveToEdition(Id);
            moved++;
        }
        other.Copies.Clear();
        return moved;
    }
}
