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
            var author1 = new Author { Name = "Author 1" };
            var author2 = new Author { Name = "Author 2" };
            db.Books.AddRange(
                new Book { Title = "Book A", Works = [new Work { Title = "Book A", WorkAuthors = [new WorkAuthor { Author = author1, Order = 0 }] }] },
                new Book { Title = "Book B", Works = [new Work { Title = "Book B", WorkAuthors = [new WorkAuthor { Author = author1, Order = 0 }] }] },
                new Book { Title = "Book C", Works = [new Work { Title = "Book C", WorkAuthors = [new WorkAuthor { Author = author2, Order = 0 }] }] }
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
            var king = new Author { Name = "Stephen King" };
            db.Authors.Add(king);
            await db.SaveChangesAsync();

            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = king.Id };
            db.Authors.Add(bachman);

            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "The Long Walk", Works = [new Work { Title = "The Long Walk", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
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
            var live = new Author { Name = "Live Author" };
            var ghost = new Author { Name = "Ghost Author" };
            db.Books.Add(new Book { Title = "Live Book", Works = [new Work { Title = "Live Book", WorkAuthors = [new WorkAuthor { Author = live, Order = 0 }] }] });
            db.Books.Add(new Book { Title = "Deleted Book", DeletedAt = DateTime.UtcNow, Works = [new Work { Title = "Deleted Book", WorkAuthors = [new WorkAuthor { Author = ghost, Order = 0 }] }] });
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
            var a = new Author { Name = "A" };
            var b = new Author { Name = "B" };
            var c = new Author { Name = "C" };

            db.Books.AddRange(
                new Book { Title = "Book A", Works = [new Work { Title = "Book A", WorkAuthors = [new WorkAuthor { Author = a, Order = 0 }], Genres = [fantasy] }] },
                new Book { Title = "Book B", Works = [new Work { Title = "Book B", WorkAuthors = [new WorkAuthor { Author = b, Order = 0 }], Genres = [fantasy, sciFi] }] },
                new Book { Title = "Book C", Works = [new Work { Title = "Book C", WorkAuthors = [new WorkAuthor { Author = c, Order = 0 }], Genres = [sciFi] }] }
            );
            await db.SaveChangesAsync();
        }

        var dashboard = await Load();

        Assert.Equal(2, dashboard.TopGenres.Count);
        Assert.Contains(dashboard.TopGenres, g => g.Genre == "Fantasy" && g.Count == 2);
        Assert.Contains(dashboard.TopGenres, g => g.Genre == "Science Fiction" && g.Count == 2);
    }
}
