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
            var fantasy = new Genre { Name = "Fantasy" };
            db.Genres.Add(fantasy);
            db.Genres.Add(new Genre { Name = "High Fantasy", ParentGenre = fantasy });
            db.Genres.Add(new Genre { Name = "Urban Fantasy", ParentGenre = fantasy });
            db.Genres.Add(new Genre { Name = "Science Fiction" });
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
        int parentId, childId;
        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var highFantasy = new Genre { Name = "High Fantasy", ParentGenre = fantasy };
            db.Genres.AddRange(fantasy, highFantasy);
            await db.SaveChangesAsync();
            parentId = fantasy.Id;
            childId = highFantasy.Id;
        }

        var vm = new GenrePickerViewModel(factory);
        await vm.InitializeAsync();

        vm.ToggleGenre(childId, true);

        Assert.Contains(childId, vm.SelectedGenreIds);
        Assert.Contains(parentId, vm.SelectedGenreIds); // parent auto-selected
    }

    [Fact]
    public async Task ToggleGenre_UnselectingParent_LeavesChildrenAlone()
    {
        var factory = new TestDbContextFactory();
        int parentId, childId;
        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var highFantasy = new Genre { Name = "High Fantasy", ParentGenre = fantasy };
            db.Genres.AddRange(fantasy, highFantasy);
            await db.SaveChangesAsync();
            parentId = fantasy.Id;
            childId = highFantasy.Id;
        }

        var vm = new GenrePickerViewModel(factory);
        await vm.InitializeAsync();
        vm.ToggleGenre(childId, true); // selects child + parent

        vm.ToggleGenre(parentId, false); // unselect parent

        Assert.DoesNotContain(parentId, vm.SelectedGenreIds);
        Assert.Contains(childId, vm.SelectedGenreIds); // child stays
    }

    [Theory]
    // Whole-word matches inside a longer candidate.
    [InlineData("Fantasy fiction", "Fantasy", true)]
    [InlineData("Epic fantasy", "Fantasy", true)]
    [InlineData("Detective and mystery stories", "Mystery", true)]
    [InlineData("Detective and mystery stories, English", "Mystery", true)]
    // Multi-word presets match only when the whole phrase appears.
    [InlineData("Science fiction novels", "Science Fiction", true)]
    [InlineData("Science", "Science Fiction", false)] // previous behaviour was wrong
    // Word-boundary protection — substring-only matches no longer fire.
    [InlineData("Romanticism", "Romance", false)]
    // Candidate-vs-preset mismatch.
    [InlineData("Romance", "Fantasy", false)]
    // Exact match (case-insensitive).
    [InlineData("MYSTERY", "Mystery", true)]
    // Empty inputs fail closed.
    [InlineData("", "Fantasy", false)]
    [InlineData("Fantasy", "", false)]
    public void FuzzyGenreMatch_MatchesCorrectly(string candidate, string preset, bool expected)
    {
        Assert.Equal(expected, GenrePickerViewModel.FuzzyGenreMatch(candidate, preset));
    }
}
