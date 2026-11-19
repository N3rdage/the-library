using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for Edition and Copy rows. Their scalar setters (Isbn, Format,
// DatePrinted, CoverUrl, Publisher, Condition, Notes, ...) go private in the
// Edition/Copy slice (E2b), so every seed that wrote them through an object
// initialiser builds through here instead; when the entity's public surface
// moves, only these two bodies change, and no other test file has to move
// again.
//
// Until E2b the bodies are plain object initialisers: there is no
// Edition.Create yet (Book.AddEdition constructs one, but also attaches it to
// the book and seeds a Copy, which not every seed wants), so a seed gets
// exactly what it passes, nothing trimmed or normalised, and an omitted
// argument leaves the entity default (a seed that passes nothing gets exactly
// `new Edition()` / `new Copy()`).
public static class TestEditions
{
    public static Edition Edition(
        string? isbn = null,
        BookFormat format = BookFormat.TradePaperback,
        int? editionNumber = null,
        DateOnly? datePrinted = null,
        DatePrecision datePrintedPrecision = DatePrecision.Day,
        string? coverUrl = null,
        bool isUserSupplied = false,
        Publisher? publisher = null,
        int? publisherId = null,
        Book? book = null,
        int? bookId = null,
        IEnumerable<Copy>? copies = null)
    {
        // Fully qualified: inside this class the simple name Edition is the method.
        var edition = new global::BookTracker.Data.Models.Edition
        {
            Isbn = isbn,
            Format = format,
            EditionNumber = editionNumber,
            DatePrinted = datePrinted,
            DatePrintedPrecision = datePrintedPrecision,
            CoverUrl = coverUrl,
            IsUserSupplied = isUserSupplied,
        };

        // Navigations and keys only when given, so an unset one stays the
        // entity default (null / 0) rather than being written as such.
        if (publisher is not null) edition.Publisher = publisher;
        if (publisherId is not null) edition.PublisherId = publisherId;
        if (book is not null) edition.Book = book;
        if (bookId is not null) edition.BookId = bookId.Value;
        if (copies is not null) edition.Copies = copies.ToList();

        return edition;
    }

    public static Copy Copy(
        BookCondition condition = BookCondition.Good,
        DateTime? dateAcquired = null,
        string? notes = null,
        Edition? edition = null,
        int? editionId = null)
    {
        // Fully qualified: inside this class the simple name Copy is the method.
        var copy = new global::BookTracker.Data.Models.Copy
        {
            Condition = condition,
            DateAcquired = dateAcquired,
            Notes = notes,
        };

        if (edition is not null) copy.Edition = edition;
        if (editionId is not null) copy.EditionId = editionId.Value;

        return copy;
    }
}
