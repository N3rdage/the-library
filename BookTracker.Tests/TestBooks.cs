using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for Book rows: Book's scalar setters (Title, Status, Rating,
// Notes, series fields) are private, so tests build a Book the way the app
// does, through Book.Create and the series methods, rather than an object
// initialiser. One place to change when the entity's public surface moves.
//
// Book.Create trims the title and trims notes / cover to null, and throws on
// a blank title — a seed that needs a blank or padded value has to say so.
// The rating is stored as given (Create has no 0–5 guard). DateAdded is
// stamped at creation and nothing in the app moves it, so no seed sets it.
public static class TestBooks
{
    public static Book Book(
        string title,
        BookStatus status = BookStatus.Unread,
        int rating = 0,
        string? notes = null,
        string? coverUrl = null,
        BookCategory category = BookCategory.Fiction,
        Series? series = null,
        int? seriesId = null,
        int? seriesOrder = null,
        string? seriesOrderDisplay = null,
        IEnumerable<Work>? works = null,
        IEnumerable<Edition>? editions = null,
        IEnumerable<Tag>? tags = null,
        DateTime? updatedAt = null,
        DateTime? deletedAt = null)
    {
        // Fully qualified: inside this class the simple name Book is the method.
        var book = global::BookTracker.Data.Models.Book.Create(title, category, status, rating, notes, coverUrl);

        // The Series overload links an unsaved series through the navigation;
        // seriesId links a saved one by key (what a seed does when it only
        // kept the id from an earlier context).
        if (series is not null)
            book.AssignToSeries(series, seriesOrder, seriesOrderDisplay);
        else if (seriesId is not null)
            book.AssignToSeries(seriesId.Value, seriesOrder, seriesOrderDisplay);
        else if (seriesOrder is not null || seriesOrderDisplay is not null)
            book.SetSeriesOrder(seriesOrder, seriesOrderDisplay);

        // Collections are assigned exactly as an initialiser would, not routed
        // through AttachWork: seeds carry explicit BookWork.Order values (and
        // Works-only seeds rely on EF's Order-0 default) that AttachWork would
        // renumber. The collection setters stay public until the collections
        // slice (E6) revisits every seed that writes them.
        if (works is not null) book.Works = works.ToList();
        if (editions is not null) book.Editions = editions.ToList();
        if (tags is not null) book.Tags = tags.ToList();

        // The timestamps have internal setters (the interceptor stamps UpdatedAt;
        // SoftDelete stamps DeletedAt); BookTracker.Data's InternalsVisibleTo lets
        // a seed pin a watermark or a tombstone without a save or a SoftDelete.
        if (updatedAt is not null) book.UpdatedAt = updatedAt.Value;
        if (deletedAt is not null) book.DeletedAt = deletedAt;

        return book;
    }
}
