using BookTracker.Shared.Catalog;

namespace BookTracker.Mobile.Cache.Presentation;

// The one-line meta under a book's title on Bookshelf's Find, AuthorWorks
// and Result pages.
public static class BookMetaFormatter
{
    // "★★★☆☆  ·  Read" — stars (if rated) then status, skipping empties.
    public static string FormatBookMeta(BookSnapshot book)
    {
        var parts = new List<string>();
        if (book.Rating > 0)
        {
            var clamped = Math.Clamp(book.Rating, 0, 5);
            parts.Add(new string('★', clamped) + new string('☆', 5 - clamped));
        }
        if (!string.IsNullOrWhiteSpace(book.Status)) parts.Add(book.Status);
        return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
    }
}
