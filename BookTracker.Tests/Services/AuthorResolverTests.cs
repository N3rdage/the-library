using BookTracker.Application.Authors;
using BookTracker.Data.Models;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Integration)]
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

        Assert.Equal(2, work.WorkAuthors.Count);
        Assert.Equal(preston, work.WorkAuthors[0].Author);
        Assert.Equal(0, work.WorkAuthors[0].Order);
        Assert.Equal(child, work.WorkAuthors[1].Author);
        Assert.Equal(1, work.WorkAuthors[1].Order);
    }

    [Fact]
    public void AssignAuthors_NoAuthorsAndNoContributors_Throws()
    {
        // Editor-only Works are legal (dictionaries, anthologies), but a Work
        // with zero contributors of any role is not.
        var work = new Work { Title = "x" };
        Assert.Throws<ArgumentException>(() => AuthorResolver.AssignAuthors(work, []));
        Assert.Throws<ArgumentException>(() =>
            AuthorResolver.AssignAuthors(work, [], additionalContributors: []));
    }

    [Fact]
    public void AssignAuthors_EditorOnly_NoAuthors_WritesContributorRow()
    {
        // Dictionary / Oxford Companion case — no Author, just an Editor.
        // Previously rejected; the relax in 2026-05-24 made this legal.
        var work = new Work { Title = "Oxford Companion to Wine" };
        var robinson = new Author { Name = "Jancis Robinson" };

        AuthorResolver.AssignAuthors(
            work,
            authors: [],
            additionalContributors: [(robinson, AuthorRole.Editor)]);

        var sole = Assert.Single(work.WorkAuthors);
        Assert.Equal(AuthorRole.Editor, sole.Role);
        Assert.Equal(robinson, sole.Author);
        Assert.Equal(0, sole.Order);
    }

    [Fact]
    public void AssignAuthors_appends_contributors_with_per_role_order_starting_at_zero()
    {
        var work = new Work { Title = "The Hobbit" };
        var tolkien = new Author { Name = "Tolkien" };
        var sergio = new Author { Name = "Sergio Cariello" };
        var mauss  = new Author { Name = "Doug Mauss" };

        AuthorResolver.AssignAuthors(
            work,
            authors: [tolkien],
            additionalContributors:
            [
                (sergio, AuthorRole.Illustrator),
                (mauss,  AuthorRole.Editor),
            ]);

        // 1 Author row + 2 non-Author rows = 3 total.
        Assert.Equal(3, work.WorkAuthors.Count);

        var author = work.WorkAuthors.Single(wa => wa.Role == AuthorRole.Author);
        Assert.Equal(tolkien, author.Author);
        Assert.Equal(0, author.Order);

        var illustrator = work.WorkAuthors.Single(wa => wa.Role == AuthorRole.Illustrator);
        Assert.Equal(sergio, illustrator.Author);
        Assert.Equal(0, illustrator.Order); // per-role Order resets to 0

        var editor = work.WorkAuthors.Single(wa => wa.Role == AuthorRole.Editor);
        Assert.Equal(mauss, editor.Author);
        Assert.Equal(0, editor.Order); // per-role Order resets to 0
    }

    [Fact]
    public void AssignAuthors_allows_same_person_in_multiple_roles()
    {
        // Tolkien is both Author and Illustrator on *The Hobbit* — the
        // composite PK (WorkId, AuthorId, Role) makes this legal, and the
        // resolver should write both rows.
        var work = new Work { Title = "The Hobbit" };
        var tolkien = new Author { Name = "Tolkien" };

        AuthorResolver.AssignAuthors(
            work,
            authors: [tolkien],
            additionalContributors: [(tolkien, AuthorRole.Illustrator)]);

        Assert.Equal(2, work.WorkAuthors.Count);
        Assert.Contains(work.WorkAuthors, wa => wa.Role == AuthorRole.Author && wa.Author == tolkien);
        Assert.Contains(work.WorkAuthors, wa => wa.Role == AuthorRole.Illustrator && wa.Author == tolkien);
    }

    [Fact]
    public void AssignAuthors_dedups_contributor_pairs_within_role()
    {
        // Same (Person, Role) pair appearing twice — should write one row.
        var work = new Work { Title = "x" };
        var asimov = new Author { Name = "Asimov" };
        var pohl = new Author { Name = "Pohl" };

        AuthorResolver.AssignAuthors(
            work,
            authors: [asimov],
            additionalContributors:
            [
                (pohl, AuthorRole.Editor),
                (pohl, AuthorRole.Editor), // duplicate of above
                (pohl, AuthorRole.Translator), // different role, kept
            ]);

        Assert.Single(work.WorkAuthors, wa => wa.Author == pohl && wa.Role == AuthorRole.Editor);
        Assert.Single(work.WorkAuthors, wa => wa.Author == pohl && wa.Role == AuthorRole.Translator);
    }

    [Fact]
    public void AssignAuthors_skips_contributors_with_role_author()
    {
        // Role=Author in the contributors list is a no-op (belongs in the
        // main authors list). The resolver silently skips so callers don't
        // accidentally double-write the lead author.
        var work = new Work { Title = "x" };
        var asimov = new Author { Name = "Asimov" };

        AuthorResolver.AssignAuthors(
            work,
            authors: [asimov],
            additionalContributors: [(asimov, AuthorRole.Author)]);

        Assert.Single(work.WorkAuthors);
        Assert.Equal(AuthorRole.Author, work.WorkAuthors[0].Role);
    }
}
