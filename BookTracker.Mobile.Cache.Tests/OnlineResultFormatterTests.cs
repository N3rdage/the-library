using BookTracker.Mobile.Cache.Presentation;
using BookTracker.Shared.Online;

namespace BookTracker.Mobile.Cache.Tests;

[Trait("Category", "Unit")]
public class OnlineResultFormatterTests
{
    private static OnlineIsbnLookup Lookup(
        OwnedWorkMatch? owned = null,
        string? title = "Foundation",
        string? subtitle = null,
        string? author = "Isaac Asimov",
        string? publisher = "Gollancz",
        int? year = 1951) =>
        new("9780586010808", title, subtitle, author, publisher, null, year, "OpenLibrary", owned);

    [Fact]
    public void Offline_IsUnknown_WithTheCacheNotFoundHeadline()
    {
        var answer = OnlineResultFormatter.Decide(Lookup(), wasOnline: false);

        Assert.Equal(OnlineResultState.Unknown, answer.State);
        Assert.Equal("Not in your library.", answer.Headline);
        Assert.Null(answer.OwnedBookId);
    }

    [Fact]
    public void OnlineButNoAnswer_IsUnknown()
    {
        var answer = OnlineResultFormatter.Decide(null, wasOnline: true);

        Assert.Equal(OnlineResultState.Unknown, answer.State);
        Assert.Equal(OnlineResultFormatter.NotFoundHeadline, answer.Headline);
        Assert.Equal("", answer.Title);
    }

    [Fact]
    public void OwnedInAnotherEdition_NamesTheWorkAndTheOwnedBook()
    {
        var answer = OnlineResultFormatter.Decide(Lookup(new OwnedWorkMatch(42, "Foundation", SameEdition: false)), true);

        Assert.Equal(OnlineResultState.OwnedOtherEdition, answer.State);
        Assert.Equal("You have this work, not this edition", answer.Headline);
        Assert.Equal("Foundation", answer.Title);
        Assert.Equal("Isaac Asimov", answer.Byline);
        Assert.Equal("Gollancz  ·  1951", answer.Details);
        Assert.Equal("", answer.Note);
        Assert.Equal(42, answer.OwnedBookId);
    }

    [Fact]
    public void OwnedSameEdition_SaysTheCacheIsBehind()
    {
        var answer = OnlineResultFormatter.Decide(Lookup(new OwnedWorkMatch(7, "Foundation", SameEdition: true)), true);

        Assert.Equal(OnlineResultState.OwnedSameEdition, answer.State);
        Assert.Equal("In your library", answer.Headline);
        Assert.Contains("behind", answer.Note);
        Assert.Contains("Sync", answer.Note);
        Assert.Equal(7, answer.OwnedBookId);
    }

    [Fact]
    public void NotOwned_CarriesTitleAuthorPublisherAndYear()
    {
        var answer = OnlineResultFormatter.Decide(Lookup(subtitle: "A Novel"), true);

        Assert.Equal(OnlineResultState.NotOwned, answer.State);
        Assert.Equal("Not in your library", answer.Headline);
        Assert.Equal("Foundation: A Novel", answer.Title);
        Assert.Equal("Isaac Asimov", answer.Byline);
        Assert.Equal("Gollancz  ·  1951", answer.Details);
        Assert.Null(answer.OwnedBookId);
    }

    [Fact]
    public void NotOwned_WithoutAuthorOrYear_LeavesThoseLinesEmpty()
    {
        var answer = OnlineResultFormatter.Decide(Lookup(author: null, year: null), true);

        Assert.Equal("", answer.Byline);
        Assert.Equal("Gollancz", answer.Details);
    }

    [Fact]
    public void NotOwned_WithoutPublisherOrYear_HasNoDetails()
    {
        var answer = OnlineResultFormatter.Decide(Lookup(author: "  ", publisher: " ", year: null), true);

        Assert.Equal("", answer.Byline);
        Assert.Equal("", answer.Details);
    }

    [Fact]
    public void Owned_WithoutProviderTitle_FallsBackToTheOwnedBookTitle()
    {
        var answer = OnlineResultFormatter.Decide(
            Lookup(new OwnedWorkMatch(3, "Foundation and Empire", SameEdition: false), title: null, year: null), true);

        Assert.Equal("Foundation and Empire", answer.Title);
        Assert.Equal("Gollancz", answer.Details);
    }

    [Fact]
    public void NotOwned_WithOnlySubtitle_UsesTheSubtitle()
    {
        var answer = OnlineResultFormatter.Decide(Lookup(title: null, subtitle: "Stories"), true);

        Assert.Equal("Stories", answer.Title);
    }
}
