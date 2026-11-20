using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for Edition and Copy rows. Their scalar setters are private, so
// every seed builds through here; when the entities' surface moves, only these
// two bodies change.
//
// The bodies use the entities' internal constructors (InternalsVisibleTo), not
// Book.AddEdition, which also attaches the edition to a book and seeds a Copy
// that not every seed wants. The constructors store exactly what they are given,
// nothing trimmed or normalised, and an omitted argument leaves the entity
// default.
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
        var edition = new global::BookTracker.Data.Models.Edition(
            isbn, format, editionNumber, datePrinted, datePrintedPrecision,
            coverUrl, isUserSupplied, publisher, copies);

        // Navigations and keys only when given, so an unset one stays the
        // entity default (null / 0) rather than being written as such.
        if (publisherId is not null) edition.ReassignPublisher(publisherId.Value);
        if (book is not null) edition.Book = book;
        if (bookId is not null) edition.MoveToBook(bookId.Value);

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
        var copy = new global::BookTracker.Data.Models.Copy(condition, dateAcquired, notes);

        if (edition is not null) copy.Edition = edition;
        if (editionId is not null) copy.MoveToEdition(editionId.Value);

        return copy;
    }
}
