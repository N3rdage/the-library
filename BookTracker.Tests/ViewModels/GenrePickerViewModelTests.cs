using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

public class GenrePickerViewModelTests
{
    [Fact]
    public async Task InitializeAsync_LoadsHierarchicalGenres()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Id = 1, Name = "Fantasy" };
            db.Genres.Add(fantasy);
            db.Genres.Add(new Genre { Id = 2, Name = "High Fantasy", ParentGenreId = 1 });
            db.Genres.Add(new Genre { Id = 3, Name = "Urban Fantasy", ParentGenreId = 1 });
            db.Genres.Add(new Genre { Id = 4, Name = "Science Fiction" });
            await db.SaveChangesAsync();
        }

        var vm = new GenrePickerViewModel(factory);
        await vm.InitializeAsync();

        Assert.Equal(2, vm.TopLevelGenres.Count);
        var fantasyNode = vm.TopLevelGenres.First(g => g.Name == "Fantasy");
        Assert.Equal(2, fantasyNode.Children.Count);
    }

    [Fact]
    public async Task ToggleGenre_SelectingChild_AutoSelectsParent()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.Genres.Add(new Genre { Id = 1, Name = "Fantasy" });
            db.Genres.Add(new Genre { Id = 2, Name = "High Fantasy", ParentGenreId = 1 });
            await db.SaveChangesAsync();
        }

        var vm = new GenrePickerViewModel(factory);
        await vm.InitializeAsync();

        vm.ToggleGenre(2, true);

        Assert.Contains(2, vm.SelectedGenreIds);
        Assert.Contains(1, vm.SelectedGenreIds); // parent auto-selected
    }

    [Fact]
    public async Task ToggleGenre_UnselectingParent_LeavesChildrenAlone()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            db.Genres.Add(new Genre { Id = 1, Name = "Fantasy" });
            db.Genres.Add(new Genre { Id = 2, Name = "High Fantasy", ParentGenreId = 1 });
            await db.SaveChangesAsync();
        }

        var vm = new GenrePickerViewModel(factory);
        await vm.InitializeAsync();
        vm.ToggleGenre(2, true); // selects child + parent

        vm.ToggleGenre(1, false); // unselect parent

        Assert.DoesNotContain(1, vm.SelectedGenreIds);
        Assert.Contains(2, vm.SelectedGenreIds); // child stays
    }

    [Theory]
    [InlineData("Fantasy fiction", "Fantasy", true)]
    [InlineData("Epic fantasy", "Fantasy", true)]
    [InlineData("Science", "Science Fiction", true)]
    [InlineData("Romance", "Fantasy", false)]
    [InlineData("", "Fantasy", false)]
    public void FuzzyGenreMatch_MatchesCorrectly(string candidate, string preset, bool expected)
    {
        Assert.Equal(expected, GenrePickerViewModel.FuzzyGenreMatch(candidate, preset));
    }
}
