using BookTracker.Application.Authors;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /authors/{id} admin write commands. Relocated from
// AuthorDetailViewModelTests when the writes moved into RenameAuthor /
// MarkAuthorAsAliasOf / PromoteAuthorToCanonical (PR6b-2).
[Trait("Category", TestCategories.Integration)]
public class AuthorAdminCommandsTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task RenameAuthor_UpdatesName_AndReportsSuccess()
    {
        int authorId;
        using (var db = _factory.CreateDbContext())
        {
            var a = new Author { Name = "Old Name" };
            db.Authors.Add(a);
            await db.SaveChangesAsync();
            authorId = a.Id;
        }

        var result = await new RenameAuthorHandler(_factory).HandleAsync(new RenameAuthor(authorId, "New Name"));

        Assert.True(result.Success);
        Assert.NotNull(result.SuccessMessage);
        using var db2 = _factory.CreateDbContext();
        Assert.Equal("New Name", db2.Authors.Single(a => a.Id == authorId).Name);
    }

    [Fact]
    public async Task RenameAuthor_RejectsNameClash_LeavesNameUnchanged()
    {
        int aliceId;
        using (var db = _factory.CreateDbContext())
        {
            db.Authors.AddRange(new Author { Name = "Alice" }, new Author { Name = "Bob" });
            await db.SaveChangesAsync();
            aliceId = db.Authors.Single(a => a.Name == "Alice").Id;
        }

        var result = await new RenameAuthorHandler(_factory).HandleAsync(new RenameAuthor(aliceId, "Bob"));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        using var db2 = _factory.CreateDbContext();
        Assert.Equal("Alice", db2.Authors.Single(a => a.Id == aliceId).Name); // unchanged
    }

    [Fact]
    public async Task MarkAuthorAsAliasOf_LinksToCanonical()
    {
        int kingId, bachmanId;
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman" };
            db.Authors.AddRange(king, bachman);
            await db.SaveChangesAsync();
            kingId = king.Id;
            bachmanId = bachman.Id;
        }

        var result = await new MarkAuthorAsAliasOfHandler(_factory).HandleAsync(new MarkAuthorAsAliasOf(bachmanId, kingId));

        Assert.True(result.Success);
        using var db2 = _factory.CreateDbContext();
        Assert.Equal(kingId, db2.Authors.Single(a => a.Id == bachmanId).CanonicalAuthorId);
    }

    [Fact]
    public async Task MarkAuthorAsAliasOf_ChainedTarget_ReRootsToTopCanonical()
    {
        // If A is an alias of B, and we mark C as alias-of-A, C should
        // actually point at B (no two-hop chains).
        int aId, bId, cId;
        using (var db = _factory.CreateDbContext())
        {
            var b = new Author { Name = "B" };
            var a = new Author { Name = "A", CanonicalAuthor = b };
            var c = new Author { Name = "C" };
            db.Authors.AddRange(b, a, c);
            await db.SaveChangesAsync();
            aId = a.Id;
            bId = b.Id;
            cId = c.Id;
        }

        var result = await new MarkAuthorAsAliasOfHandler(_factory).HandleAsync(new MarkAuthorAsAliasOf(cId, aId));

        Assert.True(result.Success);
        using var db2 = _factory.CreateDbContext();
        Assert.Equal(bId, db2.Authors.Single(a => a.Id == cId).CanonicalAuthorId);
    }

    [Fact]
    public async Task PromoteAuthorToCanonical_DropsCanonicalLink()
    {
        int bachmanId;
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthor = king };
            db.Authors.AddRange(king, bachman);
            await db.SaveChangesAsync();
            bachmanId = bachman.Id;
        }

        var result = await new PromoteAuthorToCanonicalHandler(_factory).HandleAsync(new PromoteAuthorToCanonical(bachmanId));

        Assert.True(result.Success);
        using var db2 = _factory.CreateDbContext();
        Assert.Null(db2.Authors.Single(a => a.Id == bachmanId).CanonicalAuthorId);
    }
}
