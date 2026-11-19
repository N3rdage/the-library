using BookTracker.Application.Authors;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the MergeAuthors command handler against the SQL
// container. Moved from AuthorMergeServiceTests when the merge write became a
// command (PR5, back-end refactor); the loader's LoadAsync reads stay there.
[Trait("Category", TestCategories.Integration)]
public class AuthorMergeHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<AuthorMergeResult> Merge(int winnerId, int loserId) =>
        new MergeAuthorsHandler(_factory).HandleAsync(new MergeAuthors(winnerId, loserId));

    // ─── reassignments ────────────────────────────────────────────────

    [Fact]
    public async Task Merge_reassigns_works_and_deletes_loser()
    {
        var ids = await SeedAuthorsWithWorksAsync(
            ("Douglas Preston", ["Title A", "Title B"]),
            ("Doug Preston", ["Title C"]));

        var result = await Merge(winnerId: ids[0], loserId: ids[1]);

        Assert.True(result.Success);
        Assert.Equal(1, result.WorksReassigned);
        Assert.Equal("Douglas Preston", result.WinnerName);
        Assert.Equal("Doug Preston", result.LoserName);

        using var db = _factory.CreateDbContext();
        Assert.Equal(3, db.Works.Count(w => w.WorkAuthors.Any(wa => wa.AuthorId == ids[0])));
        Assert.Null(db.Authors.FirstOrDefault(a => a.Id == ids[1]));
    }

    [Fact]
    public async Task Merge_reassigns_aliases_of_loser_to_winner()
    {
        using var db = _factory.CreateDbContext();
        var winner = Author.Create("Stephen King");
        var loser = Author.Create("S. King");
        db.Authors.AddRange(winner, loser);
        await db.SaveChangesAsync();
        // An existing pen name pointing at the loser — should reattach to winner.
        var bachman = TestAuthors.Alias("Richard Bachman", of: loser);
        db.Authors.Add(bachman);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.AliasesReassigned);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Authors.First(a => a.Id == bachman.Id);
        Assert.Equal(winner.Id, reloaded.CanonicalAuthorId);
    }

    [Fact]
    public async Task Merge_promotes_winner_when_winner_was_alias_of_loser()
    {
        // Case: winner is "Stephen King" (an alias of loser "Steve King").
        // After merge, winner must be promoted to canonical — otherwise its
        // CanonicalAuthorId dangles at the deleted loser.
        using var db = _factory.CreateDbContext();
        var loser = Author.Create("Steve King");
        db.Authors.Add(loser);
        await db.SaveChangesAsync();
        var winner = TestAuthors.Alias("Stephen King", of: loser);
        db.Authors.Add(winner);
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.True(result.WinnerPromotedToCanonical);

        using var verify = _factory.CreateDbContext();
        var reloaded = verify.Authors.First(a => a.Id == winner.Id);
        Assert.Null(reloaded.CanonicalAuthorId);
    }

    [Fact]
    public async Task Merge_removes_ignored_duplicate_rows_referencing_loser()
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

        await Merge(winnerId: ids[0], loserId: ids[1]);

        using var verify = _factory.CreateDbContext();
        Assert.Single(verify.IgnoredDuplicates);
    }

    // ─── refusals ─────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_rejects_self_merge()
    {
        var ids = await SeedAuthorsWithWorksAsync(("Douglas Preston", []));

        var result = await Merge(ids[0], ids[0]);

        Assert.False(result.Success);
        Assert.Contains("same", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Merge_rejects_missing_winner()
    {
        var ids = await SeedAuthorsWithWorksAsync(("Douglas Preston", []));

        var result = await Merge(winnerId: 9999, loserId: ids[0]);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Merge_rejects_incompatible_canonicals()
    {
        using var db = _factory.CreateDbContext();
        var z1 = Author.Create("Canonical A");
        var z2 = Author.Create("Canonical B");
        db.Authors.AddRange(z1, z2);
        await db.SaveChangesAsync();
        var d1 = TestAuthors.Alias("Doug Preston", of: z1);
        var d2 = TestAuthors.Alias("Douglas Preston", of: z2);
        db.Authors.AddRange(d1, d2);
        await db.SaveChangesAsync();

        var result = await Merge(d1.Id, d2.Id);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ─── Role-aware (Phase G) ─────────────────────────────────────────

    [Fact]
    public async Task Merge_preserves_loser_role_when_winner_has_different_role_on_same_work()
    {
        // Winner is credited as Author on Work X; loser is credited as
        // Translator on the same Work. Post-merge, winner must hold BOTH
        // roles on Work X — the loser's Translator credit should NOT be
        // dropped just because winner exists on that Work.
        using var db = _factory.CreateDbContext();
        var winner = Author.Create("Winner");
        var loser = Author.Create("Loser");
        db.Authors.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var work = TestWorks.Work(
            "Work X",
            authorships:
            [
                TestWorks.Authorship(winner),
                TestWorks.Authorship(loser, 0, AuthorRole.Translator),
            ]);
        db.Books.Add(TestBooks.Book("Book X", works: [work]));
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);

        using var verify = _factory.CreateDbContext();
        var rows = verify.WorkAuthors.Where(wa => wa.WorkId == work.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.AuthorId == winner.Id && r.Role == AuthorRole.Author);
        Assert.Contains(rows, r => r.AuthorId == winner.Id && r.Role == AuthorRole.Translator);
    }

    [Fact]
    public async Task Merge_dedupes_loser_role_when_winner_already_has_same_role()
    {
        // Both winner and loser are credited as Author on Work X. Post-merge,
        // winner has exactly one Author row on Work X (no duplicate composite-
        // PK violation, no doubled-up credit).
        using var db = _factory.CreateDbContext();
        var winner = Author.Create("Winner");
        var loser = Author.Create("Loser");
        db.Authors.AddRange(winner, loser);
        await db.SaveChangesAsync();

        var work = TestWorks.Work(
            "Work X",
            authorships:
            [
                TestWorks.Authorship(winner),
                TestWorks.Authorship(loser, 1),
            ]);
        db.Books.Add(TestBooks.Book("Book X", works: [work]));
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);

        using var verify = _factory.CreateDbContext();
        var rows = verify.WorkAuthors.Where(wa => wa.WorkId == work.Id).ToList();
        Assert.Single(rows);
        Assert.Equal(winner.Id, rows[0].AuthorId);
        Assert.Equal(AuthorRole.Author, rows[0].Role);
    }

    [Fact]
    public async Task Merge_keeps_the_loser_credits_place_in_the_order_and_its_role()
    {
        // Loser is the middle of three authors and also a translator. The
        // winner takes over both credits in the same position and role.
        using var db = _factory.CreateDbContext();
        var winner = Author.Create("Winner");
        var loser = Author.Create("Loser");
        var first = Author.Create("First");
        var last = Author.Create("Last");
        db.Authors.AddRange(winner, loser, first, last);
        await db.SaveChangesAsync();

        var work = TestWorks.Work(
            "Work X",
            authorships:
            [
                TestWorks.Authorship(first, 0),
                TestWorks.Authorship(loser, 1),
                TestWorks.Authorship(last, 2),
                TestWorks.Authorship(loser, 0, AuthorRole.Translator),
            ]);
        db.Books.Add(TestBooks.Book("Book X", works: [work]));
        await db.SaveChangesAsync();

        var result = await Merge(winner.Id, loser.Id);

        Assert.True(result.Success);
        Assert.Equal(2, result.WorksReassigned);

        using var verify = _factory.CreateDbContext();
        var rows = verify.WorkAuthors.Where(wa => wa.WorkId == work.Id)
            .Select(wa => new { wa.AuthorId, wa.Role, wa.Order })
            .ToList();
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, r => r.AuthorId == first.Id && r.Role == AuthorRole.Author && r.Order == 0);
        Assert.Contains(rows, r => r.AuthorId == winner.Id && r.Role == AuthorRole.Author && r.Order == 1);
        Assert.Contains(rows, r => r.AuthorId == last.Id && r.Role == AuthorRole.Author && r.Order == 2);
        Assert.Contains(rows, r => r.AuthorId == winner.Id && r.Role == AuthorRole.Translator && r.Order == 0);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private async Task<List<int>> SeedAuthorsWithWorksAsync(params (string Name, string[] WorkTitles)[] data)
    {
        using var db = _factory.CreateDbContext();
        var authors = new List<Author>();
        foreach (var (name, titles) in data)
        {
            var author = Author.Create(name);
            db.Authors.Add(author);
            foreach (var t in titles)
            {
                db.Books.Add(TestBooks.Book(
                    t,
                    works: [TestWorks.Work(t, author)]));
            }
            authors.Add(author);
        }
        await db.SaveChangesAsync();
        return authors.Select(a => a.Id).ToList();
    }
}
