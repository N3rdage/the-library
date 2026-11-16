using BookTracker.Application.Series;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Edit book details dialog's series dropdown read
// (moved out of BookEditDialogViewModel, #264).
[Trait("Category", TestCategories.Integration)]
public class GetSeriesOptionsHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task ReturnsEverySeriesByName_WithItsType()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Series.AddRange(
                new Series { Name = "Witcher", Type = SeriesType.Series },
                new Series { Name = "Discworld", Type = SeriesType.Collection });
            await db.SaveChangesAsync();
        }

        var options = await new GetSeriesOptionsHandler(_factory).HandleAsync(new GetSeriesOptions());

        Assert.Equal(["Discworld", "Witcher"], options.Select(o => o.Name));
        Assert.Equal(SeriesType.Collection, options[0].Type);
        Assert.Equal(SeriesType.Series, options[1].Type);
        Assert.All(options, o => Assert.True(o.Id > 0));
    }

    [Fact]
    public async Task NoSeries_ReturnsEmpty()
    {
        var options = await new GetSeriesOptionsHandler(_factory).HandleAsync(new GetSeriesOptions());

        Assert.Empty(options);
    }
}
