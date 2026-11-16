using BookTracker.Application.Wishlist;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /wishlist list read model. Pins the rows, their
// ordering (priority high first, then title) and every projected value,
// including the series name read through the optional Series link.
[Trait("Category", TestCategories.Integration)]
public class GetWishlistHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<IReadOnlyList<WishlistRow>> Load() =>
        new GetWishlistHandler(_factory).HandleAsync(new GetWishlist());

    [Fact]
    public async Task Empty_ReturnsNoRows()
    {
        Assert.Empty(await Load());
    }

    [Fact]
    public async Task Rows_OrderByPriorityDescending_ThenTitle()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.WishlistItems.AddRange(
                new WishlistItem { Title = "Mort", Author = "A", Priority = WishlistPriority.Low },
                new WishlistItem { Title = "Sourcery", Author = "A", Priority = WishlistPriority.High },
                new WishlistItem { Title = "Eric", Author = "A", Priority = WishlistPriority.Medium },
                new WishlistItem { Title = "Anathem", Author = "A", Priority = WishlistPriority.High },
                new WishlistItem { Title = "Bloodchild", Author = "A", Priority = WishlistPriority.Low });
            await db.SaveChangesAsync();
        }

        var rows = await Load();

        Assert.Equal(new[] { "Anathem", "Sourcery", "Eric", "Bloodchild", "Mort" }, rows.Select(r => r.Title));
        Assert.Equal(
            new[] { WishlistPriority.High, WishlistPriority.High, WishlistPriority.Medium, WishlistPriority.Low, WishlistPriority.Low },
            rows.Select(r => r.Priority));
    }

    [Fact]
    public async Task Row_ProjectsEveryField_WithAndWithoutSeries()
    {
        int linkedId, plainId;
        using (var db = _factory.CreateDbContext())
        {
            var foundation = new Series { Name = "Foundation", Type = SeriesType.Series, ExpectedCount = 7 };
            db.Series.Add(foundation);
            var linked = new WishlistItem
            {
                Title = "Foundation #3", Author = "Isaac Asimov", Priority = WishlistPriority.High,
                Isbn = "9780553293371", CoverUrl = "https://c/f3.jpg",
                Series = foundation, SeriesOrder = 3,
            };
            var plain = new WishlistItem { Title = "Snow Crash", Author = "Neal Stephenson", Priority = WishlistPriority.Medium };
            db.WishlistItems.AddRange(linked, plain);
            await db.SaveChangesAsync();
            linkedId = linked.Id;
            plainId = plain.Id;
        }

        var rows = await Load();

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new WishlistRow(linkedId, "Foundation #3", "Isaac Asimov", WishlistPriority.High,
                "9780553293371", "https://c/f3.jpg", "Foundation", 3),
            rows[0]);
        Assert.Equal(
            new WishlistRow(plainId, "Snow Crash", "Neal Stephenson", WishlistPriority.Medium,
                null, null, null, null),
            rows[1]);
    }
}
