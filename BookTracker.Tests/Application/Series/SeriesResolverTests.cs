using BookTracker.Application.Series;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using SeriesAggregate = BookTracker.Data.Models.Series;

namespace BookTracker.Tests;

// Unit tests for the shared Series find-or-create seam (TD-15b). Mirrors the
// AuthorResolver / PublisherResolver shape; the Add / Bulk Add save paths
// route their free-text series attachment through it. New rows go through the
// Series.Create aggregate factory (Type=Series). A blank name resolves to null
// (callers guard upstream), matching PublisherResolver — not a throw.
[Trait("Category", TestCategories.Integration)]
public class SeriesResolverTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task Resolve_NewName_TrimsAndInsertsAsSeriesType()
    {
        SeriesAggregate? resolved;
        using (var db = _factory.CreateDbContext())
        {
            resolved = await SeriesResolver.ResolveAsync(db, "  Foundation  ");
            await db.SaveChangesAsync();
        }

        Assert.NotNull(resolved);
        Assert.Equal("Foundation", resolved!.Name); // trimmed
        Assert.Equal(SeriesType.Series, resolved.Type);
        using var verify = _factory.CreateDbContext();
        Assert.Equal(1, verify.Series.Count(s => s.Name == "Foundation"));
    }

    [Fact]
    public async Task Resolve_ExistingName_ReusesRowCaseInsensitively()
    {
        int seededId;
        using (var seed = _factory.CreateDbContext())
        {
            var s = SeriesAggregate.Create("Discworld", null, SeriesType.Series, null, null);
            seed.Series.Add(s);
            await seed.SaveChangesAsync();
            seededId = s.Id;
        }

        int resolvedId;
        using (var db = _factory.CreateDbContext())
        {
            var resolved = await SeriesResolver.ResolveAsync(db, "discworld");
            await db.SaveChangesAsync();
            resolvedId = resolved!.Id;
        }

        Assert.Equal(seededId, resolvedId); // existing row, not a fresh insert
        using var verify = _factory.CreateDbContext();
        Assert.Equal(1, verify.Series.Count(s => s.Name == "Discworld")); // not duplicated
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Resolve_BlankName_ReturnsNull(string? name)
    {
        using var db = _factory.CreateDbContext();
        var resolved = await SeriesResolver.ResolveAsync(db, name);
        Assert.Null(resolved);
    }
}
