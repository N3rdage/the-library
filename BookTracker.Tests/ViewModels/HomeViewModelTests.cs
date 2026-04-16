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

        // Seed some books
        using (var db = factory.CreateDbContext())
        {
            db.Books.AddRange(
                new Book { Title = "Book A", Author = "Author 1" },
                new Book { Title = "Book B", Author = "Author 1" },
                new Book { Title = "Book C", Author = "Author 2" }
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
    public async Task InitializeAsync_WithGenres_ReturnsTopGenres()
    {
        var factory = new TestDbContextFactory();

        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var sciFi = new Genre { Name = "Science Fiction" };

            db.Books.AddRange(
                new Book { Title = "Book A", Author = "A", Genres = [fantasy] },
                new Book { Title = "Book B", Author = "B", Genres = [fantasy, sciFi] },
                new Book { Title = "Book C", Author = "C", Genres = [sciFi] }
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
