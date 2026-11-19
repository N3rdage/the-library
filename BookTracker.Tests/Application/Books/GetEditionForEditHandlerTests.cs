using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Edit Edition dialog's prefill read (moved out of
// EditionFormDialogViewModel, #264).
[Trait("Category", TestCategories.Integration)]
public class GetEditionForEditHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<EditionForEdit?> Load(int editionId) =>
        new GetEditionForEditHandler(_factory).HandleAsync(new GetEditionForEdit(editionId));

    private static List<Work> OneWork() =>
        [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = Author.Create("Pratchett"), Order = 0 }] }];

    [Fact]
    public async Task MissingId_ReturnsNull()
    {
        Assert.Null(await Load(999));
    }

    [Fact]
    public async Task LoadsEditionFields_WithBookIdAndPublisherName()
    {
        int editionId, bookId;
        using (var db = _factory.CreateDbContext())
        {
            var edition = new Edition
            {
                Isbn = "9780552131063",
                Format = BookFormat.MassMarketPaperback,
                Publisher = new Publisher { Name = "Corgi" },
                DatePrinted = new DateOnly(1987, 1, 1),
                DatePrintedPrecision = DatePrecision.Year,
                CoverUrl = "https://example.com/mort.jpg",
                Copies = [new Copy { Condition = BookCondition.Good }],
            };
            var book = TestBooks.Book("Mort", works: OneWork(), editions: [edition]);
            db.Books.Add(book);
            await db.SaveChangesAsync();
            editionId = edition.Id;
            bookId = book.Id;
        }

        var form = await Load(editionId);

        Assert.NotNull(form);
        Assert.Equal(editionId, form.Id);
        Assert.Equal(bookId, form.BookId);
        Assert.Equal("9780552131063", form.Isbn);
        Assert.Equal(BookFormat.MassMarketPaperback, form.Format);
        Assert.Equal("Corgi", form.PublisherName);
        Assert.Equal(new DateOnly(1987, 1, 1), form.DatePrinted);
        Assert.Equal(DatePrecision.Year, form.DatePrintedPrecision);
        Assert.Equal("https://example.com/mort.jpg", form.CoverUrl);
    }

    [Fact]
    public async Task NoPublisher_ReturnsNullPublisherName()
    {
        int editionId;
        using (var db = _factory.CreateDbContext())
        {
            var edition = new Edition { Format = BookFormat.Hardcover, Copies = [new Copy()] };
            db.Books.Add(TestBooks.Book("Mort", works: OneWork(), editions: [edition]));
            await db.SaveChangesAsync();
            editionId = edition.Id;
        }

        var form = await Load(editionId);

        Assert.NotNull(form);
        Assert.Null(form.PublisherName);
        Assert.Null(form.Isbn);
        Assert.Null(form.DatePrinted);
    }
}
