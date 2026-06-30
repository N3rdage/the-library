using BookTracker.Web.Components.Shared;
using Xunit;

namespace BookTracker.Tests.Components;

/// <summary>
/// Unit tests for the shared lookup-search filter (the one trim / substring / cap
/// rule the series + both publisher typeaheads feed into CreatableAutocomplete).
/// </summary>
public class LookupSearchTests
{
    // Pre-sorted, as the cached lookup lists are (loaded OrderBy Name).
    private static readonly string[] Names = ["Discworld", "Dune", "Mistborn", "The Stormlight Archive"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankQuery_ReturnsAll(string? query)
    {
        Assert.Equal(Names, LookupSearch.Filter(Names, query).ToArray());
    }

    [Fact]
    public void Substring_CaseInsensitive_PreservesOrder()
    {
        Assert.Equal(["Mistborn", "The Stormlight Archive"], LookupSearch.Filter(Names, "ST").ToArray());
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.Empty(LookupSearch.Filter(Names, "zzz"));
    }

    [Fact]
    public void CapsAtMaxResults()
    {
        var many = Enumerable.Range(0, 50).Select(i => $"Series {i:D2}").ToArray();

        Assert.Equal(LookupSearch.MaxResults, LookupSearch.Filter(many, "Series").Count());
    }
}
