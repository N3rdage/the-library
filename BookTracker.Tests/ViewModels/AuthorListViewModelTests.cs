using BookTracker.Application;
using BookTracker.Application.Authors;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// Thin unit tests for the VM-side of /authors: the in-memory free-text /
// show-aliases filtering over the GetAuthorList rows. The rollup counts and
// canonical/alias shape are covered by GetAuthorListHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class AuthorListViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static AuthorRow Canonical(int id, string name, params string[] aliases) =>
        new(id, name, null, null, aliases, 0, 0, 0);

    private static AuthorRow Alias(int id, string name, int canonicalId, string canonicalName) =>
        new(id, name, canonicalId, canonicalName, [], 0, 0, 0);

    private async Task<AuthorListViewModel> LoadedVm(params AuthorRow[] rows)
    {
        _dispatcher.Query(Arg.Any<GetAuthorList>(), Arg.Any<CancellationToken>())
            .Returns(rows);
        var vm = new AuthorListViewModel(_dispatcher);
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task LoadAsync_PopulatesAuthors()
    {
        var vm = await LoadedVm(Canonical(1, "Stephen King", "Richard Bachman"));

        Assert.False(vm.Loading);
        Assert.Single(vm.Authors);
    }

    [Fact]
    public async Task FilteredAuthors_HidesAliases_WhenShowAliasesIsFalse()
    {
        var vm = await LoadedVm(
            Canonical(1, "Stephen King", "Richard Bachman"),
            Alias(2, "Richard Bachman", 1, "Stephen King"));

        Assert.Equal(2, vm.FilteredAuthors.Count());

        vm.ShowAliases = false;
        var only = Assert.Single(vm.FilteredAuthors);
        Assert.Equal("Stephen King", only.Name);
    }

    [Fact]
    public async Task FilteredAuthors_SearchIsAliasAware_AndCaseInsensitive()
    {
        // Typing "bachman" should surface King's row even with show-aliases off,
        // because the alias name contains the term.
        var vm = await LoadedVm(
            Canonical(1, "Stephen King", "Richard Bachman"),
            Alias(2, "Richard Bachman", 1, "Stephen King"),
            Canonical(3, "Margaret Atwood"));

        vm.SearchTerm = "bachman";
        var matches = vm.FilteredAuthors.Select(a => a.Name).ToList();
        Assert.Contains("Stephen King", matches);   // matched by alias rollup
        Assert.Contains("Richard Bachman", matches); // matched by literal name
        Assert.DoesNotContain("Margaret Atwood", matches);

        // Show-aliases=false + alias-name search still surfaces the canonical.
        vm.ShowAliases = false;
        var canonicalOnly = vm.FilteredAuthors.Select(a => a.Name).ToList();
        Assert.Equal(["Stephen King"], canonicalOnly);
    }

    [Fact]
    public async Task FilteredAuthors_EmptySearch_ReturnsAllRows()
    {
        var vm = await LoadedVm(Canonical(1, "A"), Canonical(2, "B"));

        Assert.Equal(2, vm.FilteredAuthors.Count());
        vm.SearchTerm = "   ";
        Assert.Equal(2, vm.FilteredAuthors.Count()); // whitespace ignored
    }
}
