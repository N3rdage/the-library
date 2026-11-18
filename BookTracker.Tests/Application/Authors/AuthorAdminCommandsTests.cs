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
            var a = Author.Create("Old Name");
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
            db.Authors.AddRange(Author.Create("Alice"), Author.Create("Bob"));
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
            var king = Author.Create("Stephen King");
            var bachman = Author.Create("Richard Bachman");
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
            var b = Author.Create("B");
            var a = TestAuthors.Alias("A", of: b);
            var c = Author.Create("C");
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
    public async Task MarkAuthorAsAliasOf_TargetAlreadyAliasOfThisRow_IsANoOp_AndNeverPointsAtItself()
    {
        // If B is an alias of A, marking A as an alias of B re-roots to A
        // itself; the handler must leave A canonical and B pointing at A.
        int aId, bId;
        using (var db = _factory.CreateDbContext())
        {
            var a = Author.Create("A");
            var b = TestAuthors.Alias("B", of: a);
            db.Authors.AddRange(a, b);
            await db.SaveChangesAsync();
            aId = a.Id;
            bId = b.Id;
        }

        var result = await new MarkAuthorAsAliasOfHandler(_factory).HandleAsync(new MarkAuthorAsAliasOf(aId, bId));

        Assert.False(result.Success);
        using var db2 = _factory.CreateDbContext();
        Assert.Null(db2.Authors.Single(x => x.Id == aId).CanonicalAuthorId);
        Assert.Equal(aId, db2.Authors.Single(x => x.Id == bId).CanonicalAuthorId);
    }

    [Fact]
    public async Task PromoteAuthorToCanonical_DropsCanonicalLink()
    {
        int bachmanId;
        using (var db = _factory.CreateDbContext())
        {
            var king = Author.Create("Stephen King");
            var bachman = TestAuthors.Alias("Richard Bachman", of: king);
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
