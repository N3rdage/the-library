using BookTracker.Data;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Pure domain unit tests for the Series aggregate — no EF, no container. Covers
// the Create factory + UpdateDetails, and the one invariant the entity owns: a
// target ExpectedCount only sticks for an ordered Series, never a Collection.
[Trait("Category", TestCategories.Unit)]
public class SeriesAggregateTests
{
    [Fact]
    public void Create_appliesDetails_andTrims()
    {
        var series = Series.Create("  Discworld  ", "  Terry Pratchett  ", SeriesType.Series, 41, "  A flat world.  ");

        Assert.Equal("Discworld", series.Name);
        Assert.Equal("Terry Pratchett", series.Author);
        Assert.Equal(SeriesType.Series, series.Type);
        Assert.Equal(41, series.ExpectedCount);
        Assert.Equal("A flat world.", series.Description);
    }

    [Fact]
    public void Create_blankOptionalFields_becomeNull()
    {
        var series = Series.Create("Foundation", "   ", SeriesType.Series, null, "  ");

        Assert.Null(series.Author);
        Assert.Null(series.Description);
        Assert.Null(series.ExpectedCount);
    }

    [Fact]
    public void Create_blankName_throws()
    {
        Assert.Throws<DomainRuleException>(() =>
            Series.Create("   ", null, SeriesType.Series, null, null));
    }

    [Fact]
    public void Create_collectionWithExpectedCount_nullsTheCount()
    {
        // A Collection has no meaningful target count — it's dropped regardless of input.
        var series = Series.Create("Hercule Poirot", null, SeriesType.Collection, 33, null);

        Assert.Equal(SeriesType.Collection, series.Type);
        Assert.Null(series.ExpectedCount);
    }

    [Fact]
    public void UpdateDetails_overwritesAllFields()
    {
        var series = Series.Create("Old Name", "Old Author", SeriesType.Series, 5, "old");

        series.UpdateDetails("New Name", "New Author", SeriesType.Series, 9, "new");

        Assert.Equal("New Name", series.Name);
        Assert.Equal("New Author", series.Author);
        Assert.Equal(9, series.ExpectedCount);
        Assert.Equal("new", series.Description);
    }

    [Fact]
    public void UpdateDetails_seriesToCollection_dropsExpectedCount()
    {
        var series = Series.Create("Switcheroo", null, SeriesType.Series, 7, null);

        series.UpdateDetails("Switcheroo", null, SeriesType.Collection, 7, null);

        Assert.Equal(SeriesType.Collection, series.Type);
        Assert.Null(series.ExpectedCount);
    }

    [Fact]
    public void UpdateDetails_blankName_throws()
    {
        var series = Series.Create("Has A Name", null, SeriesType.Series, null, null);

        Assert.Throws<DomainRuleException>(() =>
            series.UpdateDetails("  ", null, SeriesType.Series, null, null));
    }
}
