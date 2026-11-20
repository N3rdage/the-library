using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Edit book details dialog's prefill read (moved out
// of BookEditDialogViewModel, #264).
[Trait("Category", TestCategories.Integration)]
public class GetBookEditFormHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<BookEditForm?> Load(int bookId) =>
        new GetBookEditFormHandler(_factory).HandleAsync(new GetBookEditForm(bookId));

    private static List<Work> OneWork() =>
        [TestWorks.Work("w", Author.Create("a"))];

    [Fact]
    public async Task MissingId_ReturnsNull()
    {
        Assert.Null(await Load(999));
    }

    [Fact]
    public async Task SoftDeletedBook_ReturnsNull()
    {
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var book = TestBooks.Book("Gone", deletedAt: DateTime.UtcNow, works: OneWork());
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        Assert.Null(await Load(bookId));
    }

    [Fact]
    public async Task LoadsBookLevelFields()
    {
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var book = TestBooks.Book(
                "Mort",
                category: BookCategory.NonFiction,
                coverUrl: "https://example.com/mort.jpg",
                works: OneWork());
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var form = await Load(bookId);

        Assert.NotNull(form);
        Assert.Equal(bookId, form.Id);
        Assert.Equal("Mort", form.Title);
        Assert.Equal(BookCategory.NonFiction, form.Category);
        Assert.Equal("https://example.com/mort.jpg", form.DefaultCoverArtUrl);
        Assert.Null(form.SeriesId);
    }

    [Fact]
    public async Task LoadsSeriesMembership_WithRawOrderAndDisplay()
    {
        int bookId, seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = TestSeries.Series("Witcher", SeriesType.Series);
            db.Series.Add(series);
            var book = TestBooks.Book(
                "T",
                series: series, seriesOrder: 4, seriesOrderDisplay: "4.5",
                works: OneWork());
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
            seriesId = series.Id;
        }

        var form = await Load(bookId);

        Assert.NotNull(form);
        Assert.Equal(seriesId, form.SeriesId);
        Assert.Equal(4, form.SeriesOrder);
        Assert.Equal("4.5", form.SeriesOrderDisplay);
    }

    [Fact]
    public async Task OrderLeftBehindWithoutASeries_IsStillReturnedRaw()
    {
        // A series delete SET NULLs SeriesId but leaves SeriesOrder behind. The
        // read returns it raw; the dialog decides not to show it.
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var book = TestBooks.Book("T", seriesOrder: 5, works: OneWork());
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var form = await Load(bookId);

        Assert.NotNull(form);
        Assert.Null(form.SeriesId);
        Assert.Equal(5, form.SeriesOrder);
    }
}
