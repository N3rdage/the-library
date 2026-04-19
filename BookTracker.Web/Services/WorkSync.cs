using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

// Transitional dual-write helper for PR 1 of the Work refactor.
//
// Every save site that creates or modifies a Book must call EnsureWork
// before SaveChangesAsync so the Book's mirrored Work stays in sync with
// its Title/Subtitle/Author/Genres/Series fields. PR 2 cuts over reads to
// Works and deletes this helper along with the legacy Book columns.
//
// Invariant during PR 1: every Book has exactly one linked Work.
// Compendium support (multiple Works per Book) lands in PR 2.
public static class WorkSync
{
    /// <summary>
    /// Ensures <paramref name="book"/> has exactly one linked Work that
    /// mirrors its Title/Subtitle/Author/Genres/Series. Creates the Work
    /// if missing, otherwise updates the existing one in place.
    /// </summary>
    /// <remarks>
    /// The caller must have loaded <c>book.Works</c> (and ideally
    /// <c>book.Genres</c>) before calling — for fresh Books that's free,
    /// for edited Books make sure the query <c>.Include(b => b.Works).ThenInclude(w => w.Genres)</c>.
    /// </remarks>
    public static void EnsureWork(Book book)
    {
        var work = book.Works.FirstOrDefault();
        if (work is null)
        {
            work = new Work
            {
                Title = book.Title,
                Subtitle = book.Subtitle,
                Author = book.Author,
                SeriesId = book.SeriesId,
                Series = book.Series,
                SeriesOrder = book.SeriesOrder,
            };
            foreach (var g in book.Genres) work.Genres.Add(g);
            book.Works.Add(work);
            return;
        }

        work.Title = book.Title;
        work.Subtitle = book.Subtitle;
        work.Author = book.Author;
        work.SeriesId = book.SeriesId;
        work.Series = book.Series;
        work.SeriesOrder = book.SeriesOrder;

        // Replace genres in place so EF tracks the change correctly.
        work.Genres.Clear();
        foreach (var g in book.Genres) work.Genres.Add(g);
    }
}
