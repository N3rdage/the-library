using BookTracker.Application.Home;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the home dashboard read-model handler. Relocated from
// HomeViewModelTests when the DbContext reads moved into GetHomeDashboard (PR6b);
// the VM-side bar-scaling derivation is covered by HomeViewModelTests.
[Trait("Category", TestCategories.Integration)]
public class GetHomeDashboardHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<HomeDashboard> Load() =>
        new GetHomeDashboardHandler(_factory).HandleAsync(new GetHomeDashboard());

    [Fact]
    public async Task EmptyDatabase_ReturnsZeroCounts()
    {
        var dashboard = await Load();

        Assert.Equal(0, dashboard.TotalBooks);
        Assert.Equal(0, dashboard.TotalAuthors);
        Assert.Empty(dashboard.TopAuthors);
        Assert.Empty(dashboard.TopGenres);
    }

    [Fact]
    public async Task WithBooks_ReturnsCounts()
    {
        using (var db = _factory.CreateDbContext())
        {
            // Two Works by Author 1 share the same Author row (Name is unique).
            var author1 = Author.Create("Author 1");
            var author2 = Author.Create("Author 2");
            db.Books.AddRange(
                TestBooks.Book("Book A", works: [TestWorks.Work("Book A", author1)]),
                TestBooks.Book("Book B", works: [TestWorks.Work("Book B", author1)]),
                TestBooks.Book("Book C", works: [TestWorks.Work("Book C", author2)])
            );
            await db.SaveChangesAsync();
        }

        var dashboard = await Load();

        Assert.Equal(3, dashboard.TotalBooks);
        Assert.Equal(2, dashboard.TotalAuthors);
        Assert.Equal(2, dashboard.TopAuthors.Count);
        Assert.Equal("Author 1", dashboard.TopAuthors[0].Author);
        Assert.Equal(2, dashboard.TopAuthors[0].Count);
    }

    [Fact]
    public async Task PenNamesRollUpUnderCanonical()
    {
        // Stephen King is canonical; Richard Bachman is an alias whose
        // CanonicalAuthorId points at King. A Bachman novel should add to
        // King's tally on the home dashboard.
        using (var db = _factory.CreateDbContext())
        {
            var king = Author.Create("Stephen King");
            db.Authors.Add(king);
            await db.SaveChangesAsync();

            var bachman = TestAuthors.Alias("Richard Bachman", of: king);
            db.Authors.Add(bachman);

            db.Books.Add(TestBooks.Book("Carrie", works: [TestWorks.Work("Carrie", king)]));
            db.Books.Add(TestBooks.Book("The Long Walk", works: [TestWorks.Work("The Long Walk", bachman)]));
            await db.SaveChangesAsync();
        }

        var dashboard = await Load();

        Assert.Single(dashboard.TopAuthors);
        Assert.Equal("Stephen King", dashboard.TopAuthors[0].Author);
        Assert.Equal(2, dashboard.TopAuthors[0].Count);
    }

    [Fact]
    public async Task TopAuthors_ExcludesAuthorsWithNoSurvivingBooks()
    {
        // An author whose only book is soft-deleted still has an Author-role
        // Work (WorkCount>0) but zero live books. The card ranks by distinct
        // BookCount, so without a positive-count guard a "0 books" row could
        // slip into the headline when few authors have surviving books. Guard:
        // such authors are dropped from the top-authors list.
        using (var db = _factory.CreateDbContext())
        {
            var live = Author.Create("Live Author");
            var ghost = Author.Create("Ghost Author");
            db.Books.Add(TestBooks.Book("Live Book", works: [TestWorks.Work("Live Book", live)]));
            db.Books.Add(TestBooks.Book("Deleted Book", deletedAt: DateTime.UtcNow, works: [TestWorks.Work("Deleted Book", ghost)]));
            await db.SaveChangesAsync();
        }

        var dashboard = await Load();

        Assert.Single(dashboard.TopAuthors);
        Assert.Equal("Live Author", dashboard.TopAuthors[0].Author);
        Assert.DoesNotContain(dashboard.TopAuthors, a => a.Author == "Ghost Author");
    }

    [Fact]
    public async Task WithGenres_ReturnsTopGenres()
    {
        using (var db = _factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var sciFi = new Genre { Name = "Science Fiction" };
            var a = Author.Create("A");
            var b = Author.Create("B");
            var c = Author.Create("C");

            db.Books.AddRange(
                TestBooks.Book("Book A", works: [TestWorks.Work("Book A", a, genres: [fantasy])]),
                TestBooks.Book("Book B", works: [TestWorks.Work("Book B", b, genres: [fantasy, sciFi])]),
                TestBooks.Book("Book C", works: [TestWorks.Work("Book C", c, genres: [sciFi])])
            );
            await db.SaveChangesAsync();
        }

        var dashboard = await Load();

        Assert.Equal(2, dashboard.TopGenres.Count);
        Assert.Contains(dashboard.TopGenres, g => g.Genre == "Fantasy" && g.Count == 2);
        Assert.Contains(dashboard.TopGenres, g => g.Genre == "Science Fiction" && g.Count == 2);
    }
}
