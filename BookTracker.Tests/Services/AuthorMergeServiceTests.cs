using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

// Loader-only since PR5 — the merge write moved to MergeAuthorsHandler
// (AuthorMergeHandlerTests). These cover the merge-preview reads, including the
// canonical-compatibility signal (shared with the handler via
// AuthorMergeCompatibility) that the preview surfaces.
[Trait("Category", TestCategories.Integration)]
public class AuthorMergeServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private AuthorMergeService CreateService() => new(_factory);

    [Fact]
    public async Task LoadAsync_returns_both_details_with_counts_and_samples()
    {
        var ids = await SeedAuthorsWithWorksAsync(
            ("Douglas Preston", ["Title A", "Title B", "Title C"]),
            ("Doug Preston", ["Title D"]));

        var result = await CreateService().LoadAsync(ids[0], ids[1]);

        Assert.NotNull(result.Lower);
        Assert.NotNull(result.Higher);
        Assert.Equal(3, result.Lower!.WorkCount);
        Assert.Equal(1, result.Higher!.WorkCount);
        Assert.Null(result.IncompatibilityReason);
    }

    [Fact]
    public async Task LoadAsync_reports_incompatibility_for_different_canonicals()
    {
        // "Doug Preston" aliased to Z1; "Douglas Preston" aliased to Z2.
        // Directly merging these would silently drop one of the aliasings.
        using var db = _factory.CreateDbContext();
        var z1 = new Author { Name = "Canonical A" };
        var z2 = new Author { Name = "Canonical B" };
        db.Authors.AddRange(z1, z2);
        await db.SaveChangesAsync();
        var d1 = new Author { Name = "Doug Preston", CanonicalAuthorId = z1.Id };
        var d2 = new Author { Name = "Douglas Preston", CanonicalAuthorId = z2.Id };
        db.Authors.AddRange(d1, d2);
        await db.SaveChangesAsync();

        var result = await CreateService().LoadAsync(d1.Id, d2.Id);

        Assert.NotNull(result.IncompatibilityReason);
    }

    [Fact]
    public async Task LoadAsync_permits_merge_when_loser_is_alias_of_winner()
    {
        using var db = _factory.CreateDbContext();
        var canonical = new Author { Name = "Stephen King" };
        db.Authors.Add(canonical);
        await db.SaveChangesAsync();
        var alias = new Author { Name = "Stephen King (dup)", CanonicalAuthorId = canonical.Id };
        db.Authors.Add(alias);
        await db.SaveChangesAsync();

        var result = await CreateService().LoadAsync(canonical.Id, alias.Id);

        Assert.Null(result.IncompatibilityReason);
    }

    private async Task<List<int>> SeedAuthorsWithWorksAsync(params (string Name, string[] WorkTitles)[] data)
    {
        using var db = _factory.CreateDbContext();
        var authors = new List<Author>();
        foreach (var (name, titles) in data)
        {
            var author = new Author { Name = name };
            db.Authors.Add(author);
            foreach (var t in titles)
            {
                db.Books.Add(new Book
                {
                    Title = t,
                    Works = [new Work { Title = t, WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }]
                });
            }
            authors.Add(author);
        }
        await db.SaveChangesAsync();
        return authors.Select(a => a.Id).ToList();
    }
}
