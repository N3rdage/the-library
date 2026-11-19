using BookTracker.Application.Authors;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /authors list read-model handler. Relocated from
// AuthorListViewModelTests when the DbContext reads + rollup moved into
// GetAuthorList (PR6b); the VM-side free-text / show-aliases filtering is
// covered by AuthorListViewModelTests.
[Trait("Category", TestCategories.Integration)]
public class GetAuthorListHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<IReadOnlyList<AuthorRow>> Load() =>
        new GetAuthorListHandler(_factory).HandleAsync(new GetAuthorList());

    [Fact]
    public async Task PopulatesAuthorRows_WithCanonicalAndAliasShape()
    {
        using (var db = _factory.CreateDbContext())
        {
            var king = Author.Create("Stephen King");
            var bachman = TestAuthors.Alias("Richard Bachman", of: king);
            db.Authors.AddRange(king, bachman);
            db.Books.Add(TestBooks.Book("Carrie", works: [TestWorks.Work("Carrie", king)]));
            db.Books.Add(TestBooks.Book("Thinner", works: [TestWorks.Work("Thinner", bachman)]));
            await db.SaveChangesAsync();
        }

        var rows = await Load();

        Assert.Equal(2, rows.Count);
        var kingRow = rows.Single(a => a.Name == "Stephen King");
        Assert.Null(kingRow.CanonicalAuthorId);
        Assert.Contains("Richard Bachman", kingRow.AliasNames);

        var bachmanRow = rows.Single(a => a.Name == "Richard Bachman");
        Assert.Equal(kingRow.Id, bachmanRow.CanonicalAuthorId);
    }

    [Fact]
    public async Task CanonicalCountsRollUpAliasWorks_AliasCountsAreOwnOnly()
    {
        // King has Carrie + IT (own); Bachman is an alias contributing Thinner.
        // King's row should report 3 works / 3 books / 0 series. Bachman's row
        // should report just its own — 1 / 1 / 0.
        using (var db = _factory.CreateDbContext())
        {
            var king = Author.Create("Stephen King");
            var bachman = TestAuthors.Alias("Richard Bachman", of: king);
            db.Authors.AddRange(king, bachman);
            db.Books.Add(TestBooks.Book("Carrie", works: [TestWorks.Work("Carrie", king)]));
            db.Books.Add(TestBooks.Book("It", works: [TestWorks.Work("It", king)]));
            db.Books.Add(TestBooks.Book("Thinner", works: [TestWorks.Work("Thinner", bachman)]));
            await db.SaveChangesAsync();
        }

        var rows = await Load();

        var kingRow = rows.Single(a => a.Name == "Stephen King");
        Assert.Equal(3, kingRow.WorkCount);
        Assert.Equal(3, kingRow.BookCount);
        Assert.Equal(0, kingRow.SeriesCount);

        var bachmanRow = rows.Single(a => a.Name == "Richard Bachman");
        Assert.Equal(1, bachmanRow.WorkCount);
        Assert.Equal(1, bachmanRow.BookCount);
        Assert.Equal(0, bachmanRow.SeriesCount);
    }

    [Fact]
    public async Task SeriesCount_DistinctSeriesAcrossWorks()
    {
        // Pratchett: Discworld + Bromeliad + a standalone. Series count = 2.
        using (var db = _factory.CreateDbContext())
        {
            var pratchett = Author.Create("Terry Pratchett");
            db.Authors.Add(pratchett);
            var discworld = new Series { Name = "Discworld", Type = SeriesType.Collection };
            var bromeliad = new Series { Name = "Bromeliad", Type = SeriesType.Series };
            db.Series.AddRange(discworld, bromeliad);

            db.Books.AddRange(
                TestBooks.Book("Mort", series: discworld, seriesOrder: 4, works: [TestWorks.Work("Mort", pratchett)]),
                TestBooks.Book("The Colour of Magic", series: discworld, seriesOrder: 1, works: [TestWorks.Work("The Colour of Magic", pratchett)]),
                TestBooks.Book("Truckers", series: bromeliad, seriesOrder: 1, works: [TestWorks.Work("Truckers", pratchett)]),
                TestBooks.Book("Good Omens", works: [TestWorks.Work("Good Omens", pratchett)]));
            await db.SaveChangesAsync();
        }

        var rows = await Load();

        var p = rows.Single(a => a.Name == "Terry Pratchett");
        Assert.Equal(4, p.WorkCount);
        Assert.Equal(4, p.BookCount);
        Assert.Equal(2, p.SeriesCount);
    }
}
