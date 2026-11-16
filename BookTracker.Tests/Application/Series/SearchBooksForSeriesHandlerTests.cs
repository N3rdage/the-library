using BookTracker.Application.Series;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Series edit page's "add book" search (moved out of
// SeriesEditViewModel, #265): title / work title / credited name match, the
// excluded ids, title order and the cap of 10.
[Trait("Category", TestCategories.Integration)]
public class SearchBooksForSeriesHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<IReadOnlyList<SeriesBookSearchResult>> Search(string term, params int[] excludeIds) =>
        new SearchBooksForSeriesHandler(_factory).HandleAsync(new SearchBooksForSeries(term, excludeIds));

    private async Task<int> SeedBookAsync(string title, string author, string? workTitle = null, int? seriesId = null)
    {
        using var db = _factory.CreateDbContext();
        var existing = db.Authors.FirstOrDefault(a => a.Name == author);
        var book = new Book
        {
            Title = title,
            SeriesId = seriesId,
            Works = [new Work { Title = workTitle ?? title, WorkAuthors = [new WorkAuthor { Author = existing ?? new Author { Name = author }, Order = 0 }] }],
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task BlankTerm_ReturnsEmpty()
    {
        await SeedBookAsync("Mort", "Terry Pratchett");

        Assert.Empty(await Search("  "));
    }

    [Fact]
    public async Task MatchesTitle_WorkTitle_AndCreditedName_OrderedByTitle()
    {
        await SeedBookAsync("Zebra Tales", "Terry Pratchett");               // author match
        await SeedBookAsync("Omnibus", "Someone Else", workTitle: "Pratchett Story"); // work title match
        await SeedBookAsync("A Pratchett Companion", "Someone Else");        // title match
        await SeedBookAsync("Unrelated", "Nobody");

        var result = await Search(" pratchett ");

        Assert.Equal(["A Pratchett Companion", "Omnibus", "Zebra Tales"], result.Select(r => r.Title));
    }

    [Fact]
    public async Task ExcludesGivenIds_AndCarriesCurrentSeries()
    {
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var s = new Series { Name = "Discworld", Type = SeriesType.Series };
            db.Series.Add(s);
            await db.SaveChangesAsync();
            seriesId = s.Id;
        }
        var shown = await SeedBookAsync("Mort", "Terry Pratchett", seriesId: seriesId);
        var elsewhere = await SeedBookAsync("Moving Pictures", "Terry Pratchett", seriesId: seriesId);
        var loose = await SeedBookAsync("Good Omens", "Terry Pratchett");

        var result = await Search("Pratchett", shown);

        Assert.Equal([loose, elsewhere], result.Select(r => r.Id)); // by title
        Assert.Equal(seriesId, result.Single(r => r.Id == elsewhere).CurrentSeriesId);
        Assert.Null(result.Single(r => r.Id == loose).CurrentSeriesId);
        Assert.All(result, r => Assert.Equal("Terry Pratchett", r.Author));
    }

    [Fact]
    public async Task TakesTen()
    {
        for (var i = 0; i < 12; i++)
        {
            await SeedBookAsync($"Hobbit {i:00}", "Tolkien");
        }

        var result = await Search("Hobbit");

        Assert.Equal(10, result.Count);
        Assert.Equal("Hobbit 00", result[0].Title);
        Assert.Equal("Hobbit 09", result[9].Title);
    }

    [Fact]
    public async Task MultiAuthorBook_ShowsLeadFirstAuthorRoleNames()
    {
        // Same display rule as the in-series rows, so the dropdown matches the
        // row the book becomes once added.
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var book = new Book
            {
                Title = "Relic",
                Works =
                [
                    new Work
                    {
                        Title = "Relic",
                        WorkAuthors =
                        [
                            new WorkAuthor { Author = new Author { Name = "Lincoln Child" }, Order = 1, Role = AuthorRole.Author },
                            new WorkAuthor { Author = new Author { Name = "Douglas Preston" }, Order = 0, Role = AuthorRole.Author },
                        ],
                    },
                ],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var result = Assert.Single(await Search("Relic"));

        Assert.Equal(bookId, result.Id);
        Assert.Equal("Douglas Preston, Lincoln Child", result.Author);
        var row = await new GetSeriesBookRowHandler(_factory).HandleAsync(new GetSeriesBookRow(bookId));
        Assert.Equal(result.Author, row!.Author); // search dropdown == added row
    }
}
