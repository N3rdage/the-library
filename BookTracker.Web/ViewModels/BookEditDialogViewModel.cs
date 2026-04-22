using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// Dialog-scoped VM for "Edit book details" on the View page. Covers
// Book-level fields that sit apart from the Work (title, category,
// cover URL). Notes have inline auto-save on the View page and are
// deliberately absent here. Genres and series live on the Work and
// are edited via WorkEditDialogViewModel.
public class BookEditDialogViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool NotFound { get; private set; }
    public int BookId { get; private set; }

    public string Title { get; set; } = "";
    public BookCategory Category { get; set; }
    public string? CoverUrl { get; set; }

    public async Task InitializeAsync(int bookId)
    {
        BookId = bookId;
        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(bookId);
        if (book is null) { NotFound = true; return; }

        Title = book.Title;
        Category = book.Category;
        CoverUrl = book.DefaultCoverArtUrl;
    }

    public async Task SaveAsync()
    {
        if (NotFound || string.IsNullOrWhiteSpace(Title)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var book = await db.Books.FindAsync(BookId);
        if (book is null) return;

        book.Title = Title.Trim();
        book.Category = Category;
        book.DefaultCoverArtUrl = string.IsNullOrWhiteSpace(CoverUrl) ? null : CoverUrl.Trim();

        await db.SaveChangesAsync();
    }
}
