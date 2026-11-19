using BookTracker.Application.Series;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the row the Series edit page reads back after
// AddBookToSeries (moved out of SeriesEditViewModel, #265).
[Trait("Category", TestCategories.Integration)]
public class GetSeriesBookRowHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<SeriesBookRow?> Load(int bookId) =>
        new GetSeriesBookRowHandler(_factory).HandleAsync(new GetSeriesBookRow(bookId));

    [Fact]
    public async Task MissingId_ReturnsNull()
    {
        Assert.Null(await Load(999999));
    }

    [Fact]
    public async Task ReturnsTitleOrderAndAuthorDisplay()
    {
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Stormlight", Type = SeriesType.Series };
            var book = new Book
            {
                Title = "Edgedancer",
                Series = series,
                SeriesOrder = 2,
                SeriesOrderDisplay = "2.5",
                Works =
                [
                    new Work
                    {
                        Title = "Edgedancer",
                        WorkAuthors =
                        [
                            new WorkAuthor { Author = Author.Create("Brandon Sanderson"), Order = 0, Role = AuthorRole.Author },
                            new WorkAuthor { Author = Author.Create("Ann Translator"), Order = 0, Role = AuthorRole.Translator },
                        ],
                    },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var row = await Load(bookId);

        Assert.Equal(new SeriesBookRow(bookId, "Edgedancer", "Brandon Sanderson", 2, "2.5"), row);
    }

    [Fact]
    public async Task MultiWorkBook_JoinsDistinctAuthorsAcrossWorks()
    {
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var king = Author.Create("Stephen King");
            var straub = Author.Create("Peter Straub");
            var book = new Book
            {
                Title = "Collection",
                Works =
                [
                    new Work { Title = "One", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                    new Work
                    {
                        Title = "Two",
                        WorkAuthors =
                        [
                            new WorkAuthor { Author = king, Order = 0 },
                            new WorkAuthor { Author = straub, Order = 1 },
                        ],
                    },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var row = await Load(bookId);

        Assert.NotNull(row);
        Assert.Equal(["Peter Straub", "Stephen King"], row.Author.Split(", ").Order());
    }
}
