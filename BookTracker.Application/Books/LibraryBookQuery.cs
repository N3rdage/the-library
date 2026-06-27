using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Shared filtered Book query for the Library read models — just the filter
// predicate, so the flat-list (GetLibraryBooks) and grouped (GetLibraryGroups)
// reads filter identically. The filters are all `.Any()` → SQL `EXISTS`, so no
// Includes are needed: GetLibraryGroups projects via SelectMany/GroupBy and
// GetLibraryBooks projects its page (both close-2b), so the old 4-collection
// Include cartesian (TD-2) is gone.
internal static class LibraryBookQuery
{
    public static IQueryable<Book> Filtered(BookTrackerDbContext db, LibraryFilter f)
    {
        IQueryable<Book> query = db.Books;

        if (!string.IsNullOrWhiteSpace(f.SearchTerm))
        {
            var term = f.SearchTerm.Trim();
            query = query.Where(b =>
                b.Title.Contains(term) ||
                b.Works.Any(w => w.Title.Contains(term) || w.Authors.Any(a => a.Name.Contains(term))));
        }

        if (!string.IsNullOrEmpty(f.Category) && Enum.TryParse<BookCategory>(f.Category, out var cat))
        {
            query = query.Where(b => b.Category == cat);
        }

        if (f.GenreId > 0)
            query = query.Where(b => b.Works.Any(w => w.Genres.Any(g => g.Id == f.GenreId)));
        else if (f.GenreId == -1)
            query = query.Where(b => !b.Works.Any(w => w.Genres.Any()));

        if (f.SeriesId > 0)
            query = query.Where(b => b.Works.Any(w => w.SeriesId == f.SeriesId));
        else if (f.SeriesId == -1)
            query = query.Where(b => !b.Works.Any(w => w.SeriesId.HasValue));

        if (f.TagId > 0)
            query = query.Where(b => b.Tags.Any(t => t.Id == f.TagId));

        if (f.Status.HasValue)
            query = query.Where(b => b.Status == f.Status.Value);

        if (!string.IsNullOrWhiteSpace(f.Author))
        {
            var author = f.Author.Trim();
            query = query.Where(b => b.Works.Any(w =>
                w.Authors.Any(a =>
                    a.Name == author ||
                    (a.CanonicalAuthor != null && a.CanonicalAuthor.Name == author))));
        }

        return query;
    }
}
