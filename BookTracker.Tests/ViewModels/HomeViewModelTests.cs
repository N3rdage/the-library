using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

public class HomeViewModelTests
{
    [Fact]
    public async Task InitializeAsync_EmptyDatabase_ReturnsZeroCounts()
    {
        var factory = new TestDbContextFactory();
        var vm = new HomeViewModel(factory);

        await vm.InitializeAsync();

        Assert.Equal(0, vm.TotalBooks);
        Assert.Equal(0, vm.TotalAuthors);
        Assert.Empty(vm.TopAuthors);
        Assert.Empty(vm.TopGenres);
    }

    [Fact]
    public async Task InitializeAsync_WithBooks_ReturnsCounts()
    {
        var factory = new TestDbContextFactory();

        using (var db = factory.CreateDbContext())
        {
            // Author entities are unique on Name (real schema enforces it via
            // an index, the InMemory provider doesn't but we mirror the real
            // shape here): two Works by Author 1 share the same Author row.
            var author1 = new Author { Name = "Author 1" };
            var author2 = new Author { Name = "Author 2" };
            db.Books.AddRange(
                new Book { Title = "Book A", Works = [new Work { Title = "Book A", Author = author1 }] },
                new Book { Title = "Book B", Works = [new Work { Title = "Book B", Author = author1 }] },
                new Book { Title = "Book C", Works = [new Work { Title = "Book C", Author = author2 }] }
            );
            await db.SaveChangesAsync();
        }

        var vm = new HomeViewModel(factory);
        await vm.InitializeAsync();

        Assert.Equal(3, vm.TotalBooks);
        Assert.Equal(2, vm.TotalAuthors);
        Assert.Equal(2, vm.TopAuthors.Count);
        Assert.Equal("Author 1", vm.TopAuthors[0].Author);
        Assert.Equal(2, vm.TopAuthors[0].Count);
    }

    [Fact]
    public async Task InitializeAsync_PenNamesRollUpUnderCanonical()
    {
        // Stephen King is canonical; Richard Bachman is an alias whose
        // CanonicalAuthorId points at King. A Bachman novel should add to
        // King's tally on the home dashboard.
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            db.Authors.Add(king);
            await db.SaveChangesAsync();

            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = king.Id };
            db.Authors.Add(bachman);

            db.Books.Add(new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", Author = king }] });
            db.Books.Add(new Book { Title = "The Long Walk", Works = [new Work { Title = "The Long Walk", Author = bachman }] });
            await db.SaveChangesAsync();
        }

        var vm = new HomeViewModel(factory);
        await vm.InitializeAsync();

        Assert.Single(vm.TopAuthors);
        Assert.Equal("Stephen King", vm.TopAuthors[0].Author);
        Assert.Equal(2, vm.TopAuthors[0].Count);
    }

    [Fact]
    public async Task InitializeAsync_WithGenres_ReturnsTopGenres()
    {
        var factory = new TestDbContextFactory();

        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var sciFi = new Genre { Name = "Science Fiction" };
            var a = new Author { Name = "A" };
            var b = new Author { Name = "B" };
            var c = new Author { Name = "C" };

            db.Books.AddRange(
                new Book { Title = "Book A", Works = [new Work { Title = "Book A", Author = a, Genres = [fantasy] }] },
                new Book { Title = "Book B", Works = [new Work { Title = "Book B", Author = b, Genres = [fantasy, sciFi] }] },
                new Book { Title = "Book C", Works = [new Work { Title = "Book C", Author = c, Genres = [sciFi] }] }
            );
            await db.SaveChangesAsync();
        }

        var vm = new HomeViewModel(factory);
        await vm.InitializeAsync();

        Assert.Equal(2, vm.TopGenres.Count);
        Assert.Contains(vm.TopGenres, g => g.Genre == "Fantasy" && g.Count == 2);
        Assert.Contains(vm.TopGenres, g => g.Genre == "Science Fiction" && g.Count == 2);
    }
}
