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

    [Fact]
    public async Task GetSnapshotAsync_ProjectsCoverUrlAndUnionsIsbnsAcrossLegacyAndNewTable()
    {
        // PR D shape — CoverUrl + Isbns added to the snapshot DTO so
        // the Bookshelf scan-flag can match any known ISBN, and the
        // WishlistPage can show a cover thumbnail. Isbns is a server-
        // side union of the legacy single column and the per-row
        // WishlistItemIsbn rows (PR B's schema addition), deduped
        // case-insensitively.
        using (var db = _factory.CreateDbContext())
        {
            db.WishlistItems.Add(new WishlistItem
            {
                Title = "Foundation",
                Author = "Asimov",
                Priority = WishlistPriority.High,
                Isbn = "9780553293357", // legacy single column
                CoverUrl = "https://covers.example/foundation.jpg",
                Isbns =
                [
                    // New table — overlaps with legacy on the first; the
                    // union should de-dupe to three.
                    new WishlistItemIsbn { Isbn = "9780553293357" },
                    new WishlistItemIsbn { Isbn = "9780553382570" },
                    new WishlistItemIsbn { Isbn = "9780586010822" },
                ],
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var item = Assert.Single(snapshot.Items);

        Assert.Equal("https://covers.example/foundation.jpg", item.CoverUrl);
        Assert.NotNull(item.Isbns);
        Assert.Equal(3, item.Isbns!.Count);
        Assert.Contains("9780553293357", item.Isbns);
        Assert.Contains("9780553382570", item.Isbns);
        Assert.Contains("9780586010822", item.Isbns);
    }

    [Fact]
    public async Task GetSnapshotAsync_LegacyOnlyIsbn_LandsInIsbnsList()
    {
        // Existing wishlist rows captured before PR B's schema (legacy
        // single column populated, no WishlistItemIsbn rows) still
        // surface their ISBN in the new `Isbns` list so the Bookshelf
        // scan-flag covers them without a data migration.
        using (var db = _factory.CreateDbContext())
        {
            db.WishlistItems.Add(new WishlistItem
            {
                Title = "Legacy Row",
                Author = "Pre-PR-B",
                Priority = WishlistPriority.Medium,
                Isbn = "9781234567897",
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var item = Assert.Single(snapshot.Items);
        Assert.NotNull(item.Isbns);
        Assert.Equal("9781234567897", Assert.Single(item.Isbns!));
    }
}
