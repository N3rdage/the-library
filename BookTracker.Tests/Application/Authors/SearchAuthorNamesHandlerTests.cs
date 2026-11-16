using BookTracker.Application.Authors;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the author/contributor pickers' typeahead read (moved
// out of MudAuthorPicker, MudContributorPicker and WorkEditDialogViewModel,
// #264): case-insensitive substring on the name, by name, at most 20, minus the
// names in Exclude.
[Trait("Category", TestCategories.Integration)]
public class SearchAuthorNamesHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<IReadOnlyList<string>> Search(string? query, params string[] exclude) =>
        new SearchAuthorNamesHandler(_factory).HandleAsync(new SearchAuthorNames(query, exclude));

    private async Task SeedAsync(params string[] names)
    {
        using var db = _factory.CreateDbContext();
        db.Authors.AddRange(names.Select(n => new Author { Name = n }));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MatchesSubstring_CaseInsensitive_OrderedByName()
    {
        await SeedAsync("Terry Pratchett", "Neil Gaiman", "Pratchett & Gaiman");

        var results = await Search("PRATCH");

        Assert.Equal(["Pratchett & Gaiman", "Terry Pratchett"], results);
    }

    [Fact]
    public async Task TrimsTheQuery()
    {
        await SeedAsync("Terry Pratchett", "Neil Gaiman");

        var results = await Search("  gaiman  ");

        Assert.Equal(["Neil Gaiman"], results);
    }

    [Fact]
    public async Task BlankQuery_ReturnsEveryAuthorByName()
    {
        await SeedAsync("Neil Gaiman", "Terry Pratchett", "Anne McCaffrey");

        Assert.Equal(["Anne McCaffrey", "Neil Gaiman", "Terry Pratchett"], await Search(""));
        Assert.Equal(["Anne McCaffrey", "Neil Gaiman", "Terry Pratchett"], await Search(null));
    }

    [Fact]
    public async Task ExcludesNamesAlreadyChosen()
    {
        await SeedAsync("Terry Pratchett", "Pratchett & Gaiman");

        var results = await Search("pratch", "Terry Pratchett");

        Assert.Equal(["Pratchett & Gaiman"], results);
    }

    [Fact]
    public async Task ExcludedNamesDoNotUseUpTheTwentyResultCap()
    {
        // The exclude filter runs before the cap, so a chosen name doesn't
        // cost a suggestion slot.
        var names = Enumerable.Range(1, 21).Select(i => $"Author {i:D2}").ToArray();
        await SeedAsync(names);

        var results = await Search("author", "Author 01");

        Assert.Equal(20, results.Count);
        Assert.DoesNotContain("Author 01", results);
        Assert.Equal("Author 21", results[^1]);
    }

    [Fact]
    public async Task ReturnsAtMostTwenty()
    {
        await SeedAsync(Enumerable.Range(1, 25).Select(i => $"Author {i:D2}").ToArray());

        var results = await Search("author");

        Assert.Equal(20, results.Count);
        Assert.Equal("Author 01", results[0]);
        Assert.Equal("Author 20", results[^1]);
    }
}
