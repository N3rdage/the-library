using BookTracker.Application.Books;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests;

// Unit tests for the shared Tag find-or-create seam (TD-15). Mirrors the
// AuthorResolver / PublisherResolver shape; the BookDetail tag commands,
// MarkWishlistItemBought, and BulkAdd's follow-up tag all route through it,
// so it's the single owner of tag-name normalisation.
[Trait("Category", TestCategories.Integration)]
public class TagResolverTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task FindOrCreate_NewName_NormalisesToLowercaseAndInserts()
    {
        Tag resolved;
        using (var db = _factory.CreateDbContext())
        {
            resolved = await TagResolver.FindOrCreateAsync("  Signed  ", db);
            await db.SaveChangesAsync();
        }

        Assert.Equal("signed", resolved.Name); // trimmed + lower-cased
        using var verify = _factory.CreateDbContext();
        Assert.Equal(1, verify.Tags.Count(t => t.Name == "signed"));
    }

    [Fact]
    public async Task FindOrCreate_ExistingName_ReusesRowCaseInsensitively()
    {
        using (var seed = _factory.CreateDbContext())
        {
            seed.Tags.Add(new Tag { Name = "follow-up" });
            await seed.SaveChangesAsync();
        }

        int resolvedId;
        using (var db = _factory.CreateDbContext())
        {
            var resolved = await TagResolver.FindOrCreateAsync("FOLLOW-UP", db);
            await db.SaveChangesAsync();
            resolvedId = resolved.Id;
        }

        Assert.NotEqual(0, resolvedId); // resolved to the existing row, not a fresh insert
        using var verify = _factory.CreateDbContext();
        Assert.Equal(1, verify.Tags.Count(t => t.Name == "follow-up")); // not duplicated
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task FindOrCreate_BlankName_Throws(string? name)
    {
        using var db = _factory.CreateDbContext();
        await Assert.ThrowsAsync<ArgumentException>(() => TagResolver.FindOrCreateAsync(name!, db));
    }
}
