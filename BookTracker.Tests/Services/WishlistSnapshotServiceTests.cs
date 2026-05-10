using BookTracker.Data.Models;
using BookTracker.Web.Services.Wishlist;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Integration)]
public class WishlistSnapshotServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private WishlistSnapshotService CreateService() => new(_factory);

    [Fact]
    public async Task GetSnapshotAsync_EmptyWishlist_ReturnsEmptyItems()
    {
        var snapshot = await CreateService().GetSnapshotAsync();

        Assert.Empty(snapshot.Items);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Version));
    }

    [Fact]
    public async Task GetSnapshotAsync_FieldsProjectedAndPriorityIsString()
    {
        // Status / priority fields ship as strings rather than the
        // server-side enum types — keeps BookTracker.Shared free of
        // the BookTracker.Data dependency. Same convention as
        // BookSnapshot.Status.
        using (var db = _factory.CreateDbContext())
        {
            db.WishlistItems.Add(new WishlistItem
            {
                Title = "The Dawn of Everything",
                Author = "David Graeber, David Wengrow",
                Priority = WishlistPriority.High,
                Isbn = "9780374157357",
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var item = Assert.Single(snapshot.Items);

        Assert.Equal("The Dawn of Everything", item.Title);
        Assert.Equal("David Graeber, David Wengrow", item.Author);
        Assert.Equal("High", item.Priority);
        Assert.Equal("9780374157357", item.Isbn);
    }

    [Fact]
    public async Task GetSnapshotAsync_OrderedByPriorityDescThenDateAddedAsc()
    {
        // "What to buy next" semantics — high-priority items the user
        // added a while ago bubble to the top.
        using (var db = _factory.CreateDbContext())
        {
            var earlier = DateTime.UtcNow.AddDays(-5);
            var later = DateTime.UtcNow.AddDays(-1);
            db.WishlistItems.AddRange(
                new WishlistItem { Title = "Low older", Author = "x", Priority = WishlistPriority.Low, DateAdded = earlier },
                new WishlistItem { Title = "High newer", Author = "x", Priority = WishlistPriority.High, DateAdded = later },
                new WishlistItem { Title = "High older", Author = "x", Priority = WishlistPriority.High, DateAdded = earlier },
                new WishlistItem { Title = "Medium newer", Author = "x", Priority = WishlistPriority.Medium, DateAdded = later });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var titles = snapshot.Items.Select(i => i.Title).ToList();

        Assert.Equal(["High older", "High newer", "Medium newer", "Low older"], titles);
    }

    [Fact]
    public async Task GetSnapshotAsync_PreservesSeriesLink()
    {
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Cosmere", Type = SeriesType.Collection };
            db.Series.Add(series);
            await db.SaveChangesAsync();

            db.WishlistItems.Add(new WishlistItem
            {
                Title = "Wind and Truth",
                Author = "Brandon Sanderson",
                Priority = WishlistPriority.Medium,
                SeriesId = series.Id,
                SeriesOrder = 5,
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var item = Assert.Single(snapshot.Items);

        Assert.NotNull(item.SeriesId);
        Assert.Equal(5, item.SeriesOrder);
    }
}
