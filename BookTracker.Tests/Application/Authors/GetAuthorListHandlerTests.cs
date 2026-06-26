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
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
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
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "It", Works = [new Work { Title = "It", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Thinner", Works = [new Work { Title = "Thinner", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
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
            var pratchett = new Author { Name = "Terry Pratchett" };
            db.Authors.Add(pratchett);
            var discworld = new Series { Name = "Discworld", Type = SeriesType.Collection };
            var bromeliad = new Series { Name = "Bromeliad", Type = SeriesType.Series };
            db.Series.AddRange(discworld, bromeliad);

            db.Books.AddRange(
                new Book { Title = "Mort", Works = [new Work { Title = "Mort", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 4 }] },
                new Book { Title = "The Colour of Magic", Works = [new Work { Title = "The Colour of Magic", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = discworld, SeriesOrder = 1 }] },
                new Book { Title = "Truckers", Works = [new Work { Title = "Truckers", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }], Series = bromeliad, SeriesOrder = 1 }] },
                new Book { Title = "Good Omens", Works = [new Work { Title = "Good Omens", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var rows = await Load();

        var p = rows.Single(a => a.Name == "Terry Pratchett");
        Assert.Equal(4, p.WorkCount);
        Assert.Equal(4, p.BookCount);
        Assert.Equal(2, p.SeriesCount);
    }
}
