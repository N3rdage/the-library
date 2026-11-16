using BookTracker.Application;
using BookTracker.Application.Genres;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// The picker reads its catalogue through GetGenreOptions; a substituted
// dispatcher stands in for it. The read's SQL (flat list by name, parent id +
// name) is covered by GetGenreOptionsHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class MudGenrePickerViewModelTests
{
    private const int ReferenceId = 1;
    private const int DictionariesId = 2;
    private const int AtlasesId = 3;
    private const int FantasyId = 4;

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private async Task<MudGenrePickerViewModel> LoadedVm(params GenrePickerOption[] genres)
    {
        _dispatcher.Query(Arg.Any<GetGenreOptions>(), Arg.Any<CancellationToken>())
            .Returns(genres);
        var vm = new MudGenrePickerViewModel(_dispatcher);
        await vm.InitializeAsync();
        return vm;
    }

    // The catalogue as GetGenreOptions returns it: by name, flat, with parents.
    private static readonly GenrePickerOption Atlases = new(AtlasesId, "Atlases", ReferenceId, "Reference");
    private static readonly GenrePickerOption Dictionaries = new(DictionariesId, "Dictionaries", ReferenceId, "Reference");
    private static readonly GenrePickerOption Fantasy = new(FantasyId, "Fantasy", null, null);
    private static readonly GenrePickerOption Reference = new(ReferenceId, "Reference", null, null);

    [Theory]
    // Whole-word matches inside a longer candidate.
    [InlineData("Fantasy fiction", "Fantasy", true)]
    [InlineData("Epic fantasy", "Fantasy", true)]
    [InlineData("Detective and mystery stories", "Mystery", true)]
    [InlineData("Detective and mystery stories, English", "Mystery", true)]
    // Multi-word presets match only when the whole phrase appears.
    [InlineData("Science fiction novels", "Science Fiction", true)]
    [InlineData("Science", "Science Fiction", false)]
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
        // Ported from the deleted GenrePickerViewModelTests when the
        // Bootstrap-era picker was retired — the static matcher lives on
        // MudGenrePickerViewModel now and is shared by BulkAddViewModel's
        // per-row genre hint.
        Assert.Equal(expected, MudGenrePickerViewModel.FuzzyGenreMatch(candidate, preset));
    }

    [Fact]
    public async Task InitializeAsync_LoadsFlatGenresWithParentNames()
    {
        var vm = await LoadedVm(Dictionaries, Reference);

        Assert.Equal(2, vm.AllGenres.Count);
        var dict = vm.AllGenres.Single(g => g.Name == "Dictionaries");
        Assert.Equal("Reference", dict.ParentName);
        Assert.Equal(ReferenceId, dict.ParentGenreId);
        var refGenre = vm.AllGenres.Single(g => g.Name == "Reference");
        Assert.Null(refGenre.ParentName);
        Assert.Null(refGenre.ParentGenreId);
    }

    [Fact]
    public async Task Search_SubstringCaseInsensitive_ExcludesSelected()
    {
        var vm = await LoadedVm(Atlases, Dictionaries, Fantasy, Reference);

        var results = vm.Search("DICT", []).ToList();
        Assert.Single(results);
        Assert.Equal("Dictionaries", results[0].Name);

        // already-selected ids are excluded
        var filtered = vm.Search("dict", [DictionariesId]).ToList();
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task ChipLabel_UsesParentSlashLeafForNested_LeafForTopLevel()
    {
        var vm = await LoadedVm(Dictionaries, Fantasy, Reference);

        Assert.Equal("Reference / Dictionaries", vm.ChipLabel(DictionariesId));
        Assert.Equal("Fantasy", vm.ChipLabel(FantasyId));
    }

    [Fact]
    public async Task AddGenre_AutoIncludesParentWhenChildPicked()
    {
        var vm = await LoadedVm(Dictionaries, Reference);

        var after = vm.AddGenre(DictionariesId, []);
        Assert.Contains(DictionariesId, after);
        Assert.Contains(ReferenceId, after);
    }

    [Fact]
    public async Task AddGenre_IsIdempotent()
    {
        var vm = await LoadedVm(Fantasy);

        var once = vm.AddGenre(FantasyId, []);
        var twice = vm.AddGenre(FantasyId, once);
        Assert.Single(twice);
    }

    [Fact]
    public async Task RemoveGenre_LeavesParentAlone()
    {
        // Removing a child doesn't deselect its parent — matches the existing
        // picker's behaviour (parent selection is independent of child presence).
        var vm = await LoadedVm(Dictionaries, Reference);

        var after = vm.RemoveGenre(DictionariesId, [DictionariesId, ReferenceId]);
        Assert.Contains(ReferenceId, after);
        Assert.DoesNotContain(DictionariesId, after);
    }

    [Fact]
    public async Task BuildTreeItems_GroupsChildrenUnderParents()
    {
        var vm = await LoadedVm(Atlases, Dictionaries, Fantasy, Reference);
        var tree = vm.BuildTreeItems([]);

        Assert.Equal(2, tree.Count); // Reference + Fantasy
        var referenceNode = tree.Single(t => t.Text == "Reference");
        Assert.NotNull(referenceNode.Children);
        Assert.Equal(2, referenceNode.Children!.Count);
        var fantasyNode = tree.Single(t => t.Text == "Fantasy");
        Assert.True(fantasyNode.Children is null || fantasyNode.Children.Count == 0);
    }

    [Fact]
    public async Task ApplyLookupCandidates_AddsFuzzyMatchAndItsParent()
    {
        var vm = await LoadedVm(Atlases, Dictionaries, Fantasy, Reference);

        var after = vm.ApplyLookupCandidates(["English dictionaries", "Epic fantasy"], []);

        Assert.Contains(DictionariesId, after);
        Assert.Contains(ReferenceId, after);
        Assert.Contains(FantasyId, after);
        Assert.DoesNotContain(AtlasesId, after);
    }
}
