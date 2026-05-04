using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class AuthorResolverTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public void ParseNames_SplitsOnCommaAndAmpersand()
    {
        Assert.Equal(["Douglas Preston", "Lincoln Child"], AuthorResolver.ParseNames("Douglas Preston, Lincoln Child"));
        Assert.Equal(["Douglas Preston", "Lincoln Child"], AuthorResolver.ParseNames("Douglas Preston & Lincoln Child"));
        Assert.Equal(["Murphy", "Sapir"], AuthorResolver.ParseNames("Murphy, Sapir"));
    }

    [Fact]
    public void ParseNames_TrimsWhitespaceAndDropsBlanks()
    {
        Assert.Equal(["Preston", "Child"], AuthorResolver.ParseNames("  Preston ,, &  Child   "));
    }

    [Fact]
    public void ParseNames_EmptyInputs_ReturnEmptyList()
    {
        Assert.Empty(AuthorResolver.ParseNames(null));
        Assert.Empty(AuthorResolver.ParseNames(""));
        Assert.Empty(AuthorResolver.ParseNames("  "));
    }

    [Fact]
    public async Task FindOrCreateAllAsync_ReusesExistingAuthorsAndCreatesNew()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Authors.Add(new Author { Name = "Douglas Preston" });
            await db.SaveChangesAsync();
        }

        await using var db2 = _factory.CreateDbContext();
        var authors = await AuthorResolver.FindOrCreateAllAsync(
            ["Douglas Preston", "Lincoln Child"], db2);
        await db2.SaveChangesAsync();

        // Order preserved; Preston was reused (so two authors total in DB);
        // Child is freshly created.
        Assert.Equal(2, authors.Count);
        Assert.Equal("Douglas Preston", authors[0].Name);
        Assert.Equal("Lincoln Child", authors[1].Name);

        using var verify = _factory.CreateDbContext();
        Assert.Equal(2, verify.Authors.Count());
    }

    [Fact]
    public async Task FindOrCreateAllAsync_DedupesCaseInsensitivelyAndDropsBlanks()
    {
        await using var db = _factory.CreateDbContext();
        var authors = await AuthorResolver.FindOrCreateAllAsync(
            ["Tolkien", "  ", "tolkien", "Tolkien"], db);

        // First "Tolkien" is created; subsequent variants are dedup'd; the
        // blank entry is dropped.
        Assert.Single(authors);
        Assert.Equal("Tolkien", authors[0].Name);
    }

    [Fact]
    public async Task AssignAuthors_DualWritesLeadAndJoinWithOrder()
    {
        // Direct unit test of the helper — independent of the surrounding
        // VM save paths so a regression in AssignAuthors surfaces here.
        await using var db = _factory.CreateDbContext();
        var preston = new Author { Name = "Preston" };
        var child = new Author { Name = "Child" };
        db.Authors.AddRange(preston, child);

        var work = new Work { Title = "Relic" };

        AuthorResolver.AssignAuthors(work, [preston, child]);

        Assert.Equal(preston, work.Author);
        Assert.Equal(2, work.WorkAuthors.Count);
        Assert.Equal(preston, work.WorkAuthors[0].Author);
        Assert.Equal(0, work.WorkAuthors[0].Order);
        Assert.Equal(child, work.WorkAuthors[1].Author);
        Assert.Equal(1, work.WorkAuthors[1].Order);
    }

    [Fact]
    public void AssignAuthors_EmptyList_Throws()
    {
        var work = new Work { Title = "x" };
        Assert.Throws<ArgumentException>(() => AuthorResolver.AssignAuthors(work, []));
    }
}
