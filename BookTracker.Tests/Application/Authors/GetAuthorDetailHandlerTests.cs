using BookTracker.Application.Authors;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /authors/{id} read-model handler. Relocated from
// AuthorDetailViewModelTests when the DbContext reads moved into GetAuthorDetail
// (PR6b-2). The VM-side guard/wiring is covered by AuthorDetailViewModelTests.
[Trait("Category", TestCategories.Integration)]
public class GetAuthorDetailHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<AuthorDetailResult?> Load(int authorId) =>
        new GetAuthorDetailHandler(_factory).HandleAsync(new GetAuthorDetail(authorId));

    [Fact]
    public async Task NonExistentId_ReturnsNull()
    {
        Assert.Null(await Load(99999));
    }

    [Fact]
    public async Task CanonicalRollsUpAliasWorks()
    {
        int kingId;
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
            kingId = king.Id;
        }

        var result = await Load(kingId);

        Assert.NotNull(result);
        Assert.Equal("Stephen King", result.Header.Name);
        Assert.Equal(2, result.Detail.Works.Count);
        Assert.Contains(result.Detail.Works, w => w.Title == "Carrie");
        Assert.Contains(result.Detail.Works, w => w.Title == "Thinner");
        Assert.Contains("Richard Bachman", result.Detail.AliasNames);

        // Bachman work flagged with WrittenAs; King work isn't.
        Assert.Equal("Richard Bachman", result.Detail.Works.Single(w => w.Title == "Thinner").WrittenAs);
        Assert.Null(result.Detail.Works.Single(w => w.Title == "Carrie").WrittenAs);
    }

    [Fact]
    public async Task AliasShowsOwnWorksOnly()
    {
        int bachmanId;
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
            bachmanId = bachman.Id;
        }

        var result = await Load(bachmanId);

        Assert.NotNull(result);
        Assert.Single(result.Detail.Works);
        Assert.Equal("Thinner", result.Detail.Works[0].Title);
        Assert.Empty(result.Detail.AliasNames);
        Assert.Null(result.Detail.Works[0].WrittenAs);
    }

    [Fact]
    public async Task OrdersWorksByInSeriesThenSeriesOrderThenTitle()
    {
        // Discworld 1, 3, 4 clusters before Bromeliad alphabetically, then
        // standalones tail at the end.
        int authorId;
        using (var db = _factory.CreateDbContext())
        {
            var pratchett = new Author { Name = "Terry Pratchett" };
            db.Authors.Add(pratchett);
            var discworld = new Series { Name = "Discworld", Type = SeriesType.Collection };
            var bromeliad = new Series { Name = "Bromeliad", Type = SeriesType.Series };
            db.Series.AddRange(discworld, bromeliad);

            db.Books.AddRange(
                new Book { Title = "Good Omens", Works = [new Work { Title = "Good Omens", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "Nation", Works = [new Work { Title = "Nation", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "Mort", Series = discworld, SeriesOrder = 4, Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "The Colour of Magic", Series = discworld, SeriesOrder = 1, Works = [new Work { Title = "The Colour of Magic", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "Equal Rites", Series = discworld, SeriesOrder = 3, Works = [new Work { Title = "Equal Rites", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] },
                new Book { Title = "Truckers", Series = bromeliad, SeriesOrder = 1, Works = [new Work { Title = "Truckers", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] });
            await db.SaveChangesAsync();
            authorId = pratchett.Id;
        }

        var result = await Load(authorId);

        Assert.NotNull(result);
        var titles = result.Detail.Works.Select(w => w.Title).ToList();
        Assert.Equal(
            ["Truckers", "The Colour of Magic", "Equal Rites", "Mort", "Good Omens", "Nation"],
            titles);
    }
}
