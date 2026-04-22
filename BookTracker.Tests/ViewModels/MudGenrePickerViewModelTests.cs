using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;

namespace BookTracker.Tests.ViewModels;

public class MudGenrePickerViewModelTests
{
    [Fact]
    public async Task InitializeAsync_LoadsFlatGenresWithParentNames()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var reference = new Genre { Name = "Reference" };
            db.Genres.Add(reference);
            db.Genres.Add(new Genre { Name = "Dictionaries", ParentGenre = reference });
            await db.SaveChangesAsync();
        }

        var vm = new MudGenrePickerViewModel(factory);
        await vm.InitializeAsync();

        Assert.Equal(2, vm.AllGenres.Count);
        var dict = vm.AllGenres.Single(g => g.Name == "Dictionaries");
        Assert.Equal("Reference", dict.ParentName);
        Assert.NotNull(dict.ParentGenreId);
        var refGenre = vm.AllGenres.Single(g => g.Name == "Reference");
        Assert.Null(refGenre.ParentName);
        Assert.Null(refGenre.ParentGenreId);
    }

    [Fact]
    public async Task Search_SubstringCaseInsensitive_ExcludesSelected()
    {
        var factory = new TestDbContextFactory();
        int dictionariesId;
        using (var db = factory.CreateDbContext())
        {
            var reference = new Genre { Name = "Reference" };
            var dictionaries = new Genre { Name = "Dictionaries", ParentGenre = reference };
            db.Genres.AddRange(reference, dictionaries,
                new Genre { Name = "Atlases", ParentGenre = reference },
                new Genre { Name = "Fantasy" });
            await db.SaveChangesAsync();
            dictionariesId = dictionaries.Id;
        }

        var vm = new MudGenrePickerViewModel(factory);
        await vm.InitializeAsync();

        var results = vm.Search("DICT", []).ToList();
        Assert.Single(results);
        Assert.Equal("Dictionaries", results[0].Name);

        // already-selected ids are excluded
        var filtered = vm.Search("dict", [dictionariesId]).ToList();
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task ChipLabel_UsesParentSlashLeafForNested_LeafForTopLevel()
    {
        var factory = new TestDbContextFactory();
        int dictionariesId;
        int fantasyId;
        using (var db = factory.CreateDbContext())
        {
            var reference = new Genre { Name = "Reference" };
            var dictionaries = new Genre { Name = "Dictionaries", ParentGenre = reference };
            var fantasy = new Genre { Name = "Fantasy" };
            db.Genres.AddRange(reference, dictionaries, fantasy);
            await db.SaveChangesAsync();
            dictionariesId = dictionaries.Id;
            fantasyId = fantasy.Id;
        }

        var vm = new MudGenrePickerViewModel(factory);
        await vm.InitializeAsync();

        Assert.Equal("Reference / Dictionaries", vm.ChipLabel(dictionariesId));
        Assert.Equal("Fantasy", vm.ChipLabel(fantasyId));
    }

    [Fact]
    public async Task AddGenre_AutoIncludesParentWhenChildPicked()
    {
        var factory = new TestDbContextFactory();
        int referenceId;
        int dictionariesId;
        using (var db = factory.CreateDbContext())
        {
            var reference = new Genre { Name = "Reference" };
            var dictionaries = new Genre { Name = "Dictionaries", ParentGenre = reference };
            db.Genres.AddRange(reference, dictionaries);
            await db.SaveChangesAsync();
            referenceId = reference.Id;
            dictionariesId = dictionaries.Id;
        }

        var vm = new MudGenrePickerViewModel(factory);
        await vm.InitializeAsync();

        var after = vm.AddGenre(dictionariesId, []);
        Assert.Contains(dictionariesId, after);
        Assert.Contains(referenceId, after);
    }

    [Fact]
    public async Task AddGenre_IsIdempotent()
    {
        var factory = new TestDbContextFactory();
        int fantasyId;
        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            db.Genres.Add(fantasy);
            await db.SaveChangesAsync();
            fantasyId = fantasy.Id;
        }

        var vm = new MudGenrePickerViewModel(factory);
        await vm.InitializeAsync();

        var once = vm.AddGenre(fantasyId, []);
        var twice = vm.AddGenre(fantasyId, once);
        Assert.Single(twice);
    }

    [Fact]
    public async Task RemoveGenre_LeavesParentAlone()
    {
        // Removing a child doesn't deselect its parent — matches the existing
        // picker's behaviour (parent selection is independent of child presence).
        var factory = new TestDbContextFactory();
        int referenceId;
        int dictionariesId;
        using (var db = factory.CreateDbContext())
        {
            var reference = new Genre { Name = "Reference" };
            var dictionaries = new Genre { Name = "Dictionaries", ParentGenre = reference };
            db.Genres.AddRange(reference, dictionaries);
            await db.SaveChangesAsync();
            referenceId = reference.Id;
            dictionariesId = dictionaries.Id;
        }

        var vm = new MudGenrePickerViewModel(factory);
        await vm.InitializeAsync();

        var after = vm.RemoveGenre(dictionariesId, [dictionariesId, referenceId]);
        Assert.Contains(referenceId, after);
        Assert.DoesNotContain(dictionariesId, after);
    }

    [Fact]
    public async Task BuildTreeItems_GroupsChildrenUnderParents()
    {
        var factory = new TestDbContextFactory();
        using (var db = factory.CreateDbContext())
        {
            var reference = new Genre { Name = "Reference" };
            db.Genres.AddRange(
                reference,
                new Genre { Name = "Dictionaries", ParentGenre = reference },
                new Genre { Name = "Atlases", ParentGenre = reference },
                new Genre { Name = "Fantasy" });
            await db.SaveChangesAsync();
        }

        var vm = new MudGenrePickerViewModel(factory);
        await vm.InitializeAsync();
        var tree = vm.BuildTreeItems([]);

        Assert.Equal(2, tree.Count); // Reference + Fantasy
        var referenceNode = tree.Single(t => t.Text == "Reference");
        Assert.NotNull(referenceNode.Children);
        Assert.Equal(2, referenceNode.Children!.Count);
        var fantasyNode = tree.Single(t => t.Text == "Fantasy");
        Assert.True(fantasyNode.Children is null || fantasyNode.Children.Count == 0);
    }
}
