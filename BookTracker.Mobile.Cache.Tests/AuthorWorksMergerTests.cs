using BookTracker.Mobile.Cache.Presentation;
using BookTracker.Shared.Catalog;
using BookTracker.Shared.Online;

namespace BookTracker.Mobile.Cache.Tests;

[Trait("Category", "Unit")]
public class AuthorWorksMergerTests
{
    private static BookSnapshot Book(int id, string title, params string[] workTitles) =>
        new(id, title, "Isaac Asimov", [], "Read", 4, [], null, null,
            Works: workTitles.Length == 0
                ? null
                : workTitles.Select((t, i) => new WorkSnapshot(100 + i, t, "Isaac Asimov")).ToList());

    private static OnlineAuthorWork Online(string? title, int? ownedBookId = null, int? year = null) =>
        new($"/works/OL{Math.Abs((title ?? "").GetHashCode())}W", title, year, null, null, ownedBookId);

    [Fact]
    public void AllOwned_ListsOnlyTheCachedBooks()
    {
        var owned = new[] { Book(1, "Foundation"), Book(2, "I, Robot") };
        var online = new[] { Online("Foundation", 1), Online("I, Robot", 2) };

        var rows = AuthorWorksMerger.Merge(owned, online);

        Assert.Equal(["Foundation", "I, Robot"], rows.Select(r => r.Title));
        Assert.All(rows, r => Assert.True(r.IsOwned));
        Assert.Same(owned[0], rows[0].OwnedBook);
        Assert.Equal("2 works · 2 owned", AuthorWorksMerger.Summary(rows));
    }

    [Fact]
    public void NoneOwned_ListsEveryOnlineWorkAsMissing_InTheEndpointOrder()
    {
        var online = new[] { Online("Foundation", year: 1951), Online("The Gods Themselves") };

        var rows = AuthorWorksMerger.Merge([], online);

        Assert.Equal(["Foundation", "The Gods Themselves"], rows.Select(r => r.Title));
        Assert.All(rows, r => Assert.False(r.IsOwned));
        Assert.All(rows, r => Assert.Null(r.OwnedBook));
        Assert.Equal("Not owned  ·  1951", AuthorWorksMerger.MissingMeta(rows[0]));
        Assert.Equal("Not owned", AuthorWorksMerger.MissingMeta(rows[1]));
        Assert.Equal("2 works · 0 owned", AuthorWorksMerger.Summary(rows));
    }

    [Fact]
    public void OwnedFirst_ThenMissing()
    {
        var owned = new[] { Book(1, "Foundation") };
        var online = new[] { Online("The End of Eternity"), Online("Foundation", 1), Online("Nemesis") };

        var rows = AuthorWorksMerger.Merge(owned, online);

        Assert.Equal(["Foundation", "The End of Eternity", "Nemesis"], rows.Select(r => r.Title));
        Assert.Equal([true, false, false], rows.Select(r => r.IsOwned));
    }

    [Fact]
    public void DuplicateById_IsNotListedTwice_EvenWhenTheTitlesDiffer()
    {
        // The cache holds the Book under the library's title; Open Library
        // spells the work differently, and Bookcase matched it by id.
        var owned = new[] { Book(7, "Foundation (Foundation #1)") };
        var online = new[] { Online("Foundation", 7) };

        var rows = AuthorWorksMerger.Merge(owned, online);

        var row = Assert.Single(rows);
        Assert.Equal(7, row.OwnedBookId);
        Assert.NotNull(row.OwnedBook);
    }

    [Fact]
    public void DuplicateByNormalisedTitle_IsNotListedTwice_AcrossCasingAndPunctuation()
    {
        // No OwnedBookId from the server (say the Book was added after its
        // match ran), but the cache already holds it.
        var owned = new[] { Book(3, "I, Robot"), Book(4, "The Caves of Steel") };
        var online = new[] { Online("i robot"), Online("CAVES OF STEEL!"), Online("Pebble in the Sky") };

        var rows = AuthorWorksMerger.Merge(owned, online);

        Assert.Equal(["I, Robot", "The Caves of Steel", "Pebble in the Sky"], rows.Select(r => r.Title));
        Assert.False(rows[2].IsOwned);
    }

    [Fact]
    public void DuplicateByNormalisedTitle_MatchesAWorkInsideAnOwnedCompendium()
    {
        var owned = new[] { Book(5, "The Complete Robot Novels", "The Caves of Steel", "The Naked Sun") };
        var online = new[] { Online("The Naked Sun"), Online("Robots of Dawn") };

        var rows = AuthorWorksMerger.Merge(owned, online);

        Assert.Equal(["The Complete Robot Novels", "Robots of Dawn"], rows.Select(r => r.Title));
    }

    [Fact]
    public void OnlineRepeatsAndUntitledWorks_AreDropped()
    {
        var online = new[] { Online("Nightfall"), Online("Nightfall."), Online(null), Online("   ") };

        var rows = AuthorWorksMerger.Merge([], online);

        Assert.Equal(["Nightfall"], rows.Select(r => r.Title));
    }

    [Fact]
    public void OwnedOnTheServerButNotInTheCache_IsAnOwnedRowWithoutACachedBook()
    {
        var owned = new[] { Book(1, "Foundation") };
        var online = new[] { Online("Second Foundation", 9) };

        var rows = AuthorWorksMerger.Merge(owned, online);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[1].IsOwned);
        Assert.Equal(9, rows[1].OwnedBookId);
        Assert.Null(rows[1].OwnedBook);
        Assert.Empty(AuthorWorksMerger.Filter(rows, missingOnly: true));
    }

    [Fact]
    public void EmptyOnlineList_LeavesTheOwnedList()
    {
        var owned = new[] { Book(1, "Foundation"), Book(2, "I, Robot") };

        Assert.Equal(["Foundation", "I, Robot"], AuthorWorksMerger.Merge(owned, []).Select(r => r.Title));
        Assert.Equal(["Foundation", "I, Robot"], AuthorWorksMerger.Merge(owned, null).Select(r => r.Title));
    }

    [Fact]
    public void MissingOnlyFilter_KeepsOnlyTheWorksNotOwned_AndAllKeepsEverything()
    {
        var owned = new[] { Book(1, "Foundation") };
        var online = new[] { Online("Foundation", 1), Online("Nemesis"), Online("Nightfall") };
        var rows = AuthorWorksMerger.Merge(owned, online);

        Assert.Equal(["Nemesis", "Nightfall"], AuthorWorksMerger.Filter(rows, missingOnly: true).Select(r => r.Title));
        Assert.Equal(3, AuthorWorksMerger.Filter(rows, missingOnly: false).Count);
    }

    [Theory]
    [InlineData("The Caves of Steel", "caves of steel")]
    [InlineData("  I,   Robot!  ", "i robot")]
    [InlineData("An Ode", "ode")]
    [InlineData("A", "a")]
    [InlineData("Theory of Everything", "theory of everything")]
    [InlineData(null, "")]
    public void NormaliseTitle_FollowsBookcasesRule(string? input, string expected) =>
        Assert.Equal(expected, AuthorWorksMerger.NormaliseTitle(input));

    [Fact]
    public void Summary_SingularWork()
    {
        var rows = AuthorWorksMerger.Merge([], [Online("Nightfall")]);
        Assert.Equal("1 work · 0 owned", AuthorWorksMerger.Summary(rows));
    }
}
