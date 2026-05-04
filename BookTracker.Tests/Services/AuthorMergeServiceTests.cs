using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

public class AuthorMergeServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private AuthorMergeService CreateService() => new(_factory);

    // ─── LoadAsync ────────────────────────────────────────────────────

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

    // ─── MergeAsync — reassignments ───────────────────────────────────

    [Fact]
    public async Task MergeAsync_reassigns_works_and_deletes_loser()
    {
        var ids = await SeedAuthorsWithWorksAsync(
            ("Douglas Preston", ["Title A", "Title B"]),
            ("Doug Preston", ["Title C"]));

        var result = await CreateService().MergeAsync(winnerId: ids[0], loserId: ids[1]);

        Assert.True(result.Success);
        Assert.Equal(1, result.WorksReassigned);
        Assert.Equal("Douglas Preston", result.WinnerName);
        Assert.Equal("Doug Preston", result.LoserName);

        using var db = _factory.CreateDbContext();
        Assert.Equal(3, db.Works.Count(w => w.WorkAuthors.Any(wa => wa.AuthorId == ids[0])));
        Assert.Null(db.Authors.FirstOrDefault(a => a.Id == ids[1]));
    }

    [Fact]
    public async Task MergeAsync_reassigns_aliases_of_loser_to_winner()
    {
        using var db = _factory.CreateDbContext();
        var winner = new Author { Name = "Stephen King" };
        var loser = new Author { Name = "S. King" };
        db.Authors.AddRange(winner, loser);
        await db.SaveChangesAsync();
        // An existing pen name pointing at the loser — should reattach to winner.
        var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = loser.Id };
        db.Authors.Add(bachman);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.AliasesReassigned);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Authors.First(a => a.Id == bachman.Id);
        Assert.Equal(winner.Id, reloaded.CanonicalAuthorId);
    }

    [Fact]
    public async Task MergeAsync_promotes_winner_when_winner_was_alias_of_loser()
    {
        // Case: winner is "Stephen King" (an alias of loser "Steve King").
        // After merge, winner must be promoted to canonical — otherwise its
        // CanonicalAuthorId dangles at the deleted loser.
        using var db = _factory.CreateDbContext();
        var loser = new Author { Name = "Steve King" };
        db.Authors.Add(loser);
        await db.SaveChangesAsync();
        var winner = new Author { Name = "Stephen King", CanonicalAuthorId = loser.Id };
        db.Authors.Add(winner);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.True(result.WinnerPromotedToCanonical);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Authors.First(a => a.Id == winner.Id);
        Assert.Null(reloaded.CanonicalAuthorId);
    }

    [Fact]
    public async Task MergeAsync_removes_ignored_duplicate_rows_referencing_loser()
    {
        var ids = await SeedAuthorsWithWorksAsync(
            ("Douglas Preston", ["Title A"]),
            ("Doug Preston", ["Title B"]),
            ("Third Person", []));

        using (var db = _factory.CreateDbContext())
        {
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Author,
                LowerId = Math.Min(ids[0], ids[1]),
                HigherId = Math.Max(ids[0], ids[1])
            });
            // Unrelated pair that shouldn't be deleted.
            db.IgnoredDuplicates.Add(new IgnoredDuplicate
            {
                EntityType = DuplicateEntityType.Author,
                LowerId = Math.Min(ids[0], ids[2]),
                HigherId = Math.Max(ids[0], ids[2])
            });
            await db.SaveChangesAsync();
        }

        await CreateService().MergeAsync(winnerId: ids[0], loserId: ids[1]);

        using var verify = _factory.CreateDbContext();
        Assert.Single(verify.IgnoredDuplicates);
    }

    // ─── MergeAsync — refusals ────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_rejects_self_merge()
    {
        var ids = await SeedAuthorsWithWorksAsync(("Douglas Preston", []));

        var result = await CreateService().MergeAsync(ids[0], ids[0]);

        Assert.False(result.Success);
        Assert.Contains("same", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeAsync_rejects_missing_winner()
    {
        var ids = await SeedAuthorsWithWorksAsync(("Douglas Preston", []));

        var result = await CreateService().MergeAsync(winnerId: 9999, loserId: ids[0]);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MergeAsync_rejects_incompatible_canonicals()
    {
        using var db = _factory.CreateDbContext();
        var z1 = new Author { Name = "Canonical A" };
        var z2 = new Author { Name = "Canonical B" };
        db.Authors.AddRange(z1, z2);
        await db.SaveChangesAsync();
        var d1 = new Author { Name = "Doug Preston", CanonicalAuthorId = z1.Id };
        var d2 = new Author { Name = "Douglas Preston", CanonicalAuthorId = z2.Id };
        db.Authors.AddRange(d1, d2);
        await db.SaveChangesAsync();

        var result = await CreateService().MergeAsync(d1.Id, d2.Id);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

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
