using BookTracker.Data;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Pure domain unit tests for the Work aggregate — no EF, no container. Covers the
// ref-count relationship (Create/AppearsIn/RemoveFrom), the field methods, and
// AssignAuthorship (the authorship-building logic that moved off AuthorResolver).
[Trait("Category", TestCategories.Unit)]
public class WorkAggregateTests
{
    // --- Lifecycle / relationship -------------------------------------------

    [Fact]
    public void Create_bornAttachedToFirstBook_withFieldsAndAuthorship()
    {
        var book = new Book { Id = 1, Title = "B" };
        var author = new Author { Name = "Le Guin" };

        var work = Work.Create(book, "  A Wizard of Earthsea  ", "  ", new DateOnly(1968, 1, 1), DatePrecision.Year, [author]);

        Assert.Equal("A Wizard of Earthsea", work.Title);
        Assert.Null(work.Subtitle); // blank → null
        Assert.Equal(new DateOnly(1968, 1, 1), work.FirstPublishedDate);
        Assert.Equal(DatePrecision.Year, work.FirstPublishedDatePrecision);
        Assert.Single(work.Books);
        Assert.Same(book, work.Books[0]);          // ref count starts at 1
        var wa = Assert.Single(work.WorkAuthors);
        Assert.Equal(AuthorRole.Author, wa.Role);
        Assert.Same(author, wa.Author);
    }

    [Fact]
    public void Create_blankTitle_throws()
    {
        var book = new Book { Id = 1 };
        Assert.Throws<DomainRuleException>(() =>
            Work.Create(book, "  ", null, null, DatePrecision.Day, [new Author { Name = "A" }]));
    }

    [Fact]
    public void Create_noContributors_throws()
    {
        var book = new Book { Id = 1 };
        Assert.Throws<DomainRuleException>(() =>
            Work.Create(book, "T", null, null, DatePrecision.Day, []));
    }

    [Fact]
    public void AppearsIn_addsBook_andIsIdempotent()
    {
        var b1 = new Book { Id = 1 };
        var b2 = new Book { Id = 2 };
        var work = Work.Create(b1, "T", null, null, DatePrecision.Day, [new Author { Name = "A" }]);

        Assert.True(work.AppearsIn(b2));
        Assert.Equal(2, work.Books.Count);
        Assert.False(work.AppearsIn(b2)); // already there → no-op
        Assert.Equal(2, work.Books.Count);
    }

    [Fact]
    public void RemoveFrom_lastBook_reportsOrphaned()
    {
        var b1 = new Book { Id = 1 };
        var work = Work.Create(b1, "T", null, null, DatePrecision.Day, [new Author { Name = "A" }]);

        var orphaned = work.RemoveFrom(b1);

        Assert.True(orphaned);          // ref count hit 0
        Assert.Empty(work.Books);
    }

    [Fact]
    public void RemoveFrom_whenOnOtherBooks_isNotOrphaned()
    {
        var b1 = new Book { Id = 1 };
        var b2 = new Book { Id = 2 };
        var work = Work.Create(b1, "T", null, null, DatePrecision.Day, [new Author { Name = "A" }]);
        work.AppearsIn(b2);

        var orphaned = work.RemoveFrom(b1);

        Assert.False(orphaned);
        Assert.Single(work.Books);
        Assert.Equal(2, work.Books[0].Id);
    }

    // --- Field methods -------------------------------------------------------

    [Fact]
    public void UpdateDetails_blankTitle_throws()
    {
        var work = new Work { Title = "old" };
        Assert.Throws<DomainRuleException>(() => work.UpdateDetails("   ", null));
    }

    [Fact]
    public void AssignToSeries_then_ClearSeries()
    {
        var work = new Work { Title = "T" };
        work.AssignToSeries(7, 4, "4.5");
        Assert.Equal(7, work.SeriesId);
        Assert.Equal(4, work.SeriesOrder);
        Assert.Equal("4.5", work.SeriesOrderDisplay);

        work.ClearSeries();
        Assert.Null(work.SeriesId);
        Assert.Null(work.SeriesOrder);
        Assert.Null(work.SeriesOrderDisplay);
    }

    [Fact]
    public void SetGenres_replacesTheCollection()
    {
        var work = new Work { Title = "T", Genres = { new Genre { Id = 1, Name = "Old" } } };
        var fantasy = new Genre { Id = 2, Name = "Fantasy" };
        work.SetGenres([fantasy]);
        Assert.Single(work.Genres);
        Assert.Same(fantasy, work.Genres[0]);
    }

    // --- AssignAuthorship (moved from AuthorResolver.AssignAuthors) ----------

    [Fact]
    public void AssignAuthorship_writesAuthorsWithOrder()
    {
        var work = new Work { Title = "Relic" };
        var preston = new Author { Name = "Preston" };
        var child = new Author { Name = "Child" };

        work.AssignAuthorship([preston, child]);

        Assert.Equal(2, work.WorkAuthors.Count);
        Assert.Same(preston, work.WorkAuthors[0].Author);
        Assert.Equal(0, work.WorkAuthors[0].Order);
        Assert.Same(child, work.WorkAuthors[1].Author);
        Assert.Equal(1, work.WorkAuthors[1].Order);
    }

    [Fact]
    public void AssignAuthorship_noContributors_throwsDomainRule()
    {
        var work = new Work { Title = "x" };
        Assert.Throws<DomainRuleException>(() => work.AssignAuthorship([]));
        Assert.Throws<DomainRuleException>(() => work.AssignAuthorship([], additionalContributors: []));
    }

    [Fact]
    public void AssignAuthorship_editorOnly_isValid()
    {
        var work = new Work { Title = "Oxford Companion to Wine" };
        var robinson = new Author { Name = "Jancis Robinson" };

        work.AssignAuthorship([], [(robinson, AuthorRole.Editor)]);

        var sole = Assert.Single(work.WorkAuthors);
        Assert.Equal(AuthorRole.Editor, sole.Role);
        Assert.Same(robinson, sole.Author);
        Assert.Equal(0, sole.Order);
    }

    [Fact]
    public void AssignAuthorship_contributorsGetPerRoleOrderFromZero()
    {
        var work = new Work { Title = "The Hobbit" };
        var tolkien = new Author { Name = "Tolkien" };
        var sergio = new Author { Name = "Sergio Cariello" };
        var mauss = new Author { Name = "Doug Mauss" };

        work.AssignAuthorship([tolkien], [(sergio, AuthorRole.Illustrator), (mauss, AuthorRole.Editor)]);

        Assert.Equal(3, work.WorkAuthors.Count);
        Assert.Equal(0, work.WorkAuthors.Single(wa => wa.Role == AuthorRole.Author).Order);
        Assert.Equal(0, work.WorkAuthors.Single(wa => wa.Role == AuthorRole.Illustrator).Order);
        Assert.Equal(0, work.WorkAuthors.Single(wa => wa.Role == AuthorRole.Editor).Order);
    }

    [Fact]
    public void AssignAuthorship_allowsSamePersonInMultipleRoles()
    {
        var work = new Work { Title = "The Hobbit" };
        var tolkien = new Author { Name = "Tolkien" };

        work.AssignAuthorship([tolkien], [(tolkien, AuthorRole.Illustrator)]);

        Assert.Equal(2, work.WorkAuthors.Count);
        Assert.Contains(work.WorkAuthors, wa => wa.Role == AuthorRole.Author && wa.Author == tolkien);
        Assert.Contains(work.WorkAuthors, wa => wa.Role == AuthorRole.Illustrator && wa.Author == tolkien);
    }

    [Fact]
    public void AssignAuthorship_dedupsContributorPairsWithinRole_andSkipsAuthorRole()
    {
        var work = new Work { Title = "x" };
        var asimov = new Author { Name = "Asimov" };
        var pohl = new Author { Name = "Pohl" };

        work.AssignAuthorship([asimov],
        [
            (pohl, AuthorRole.Editor),
            (pohl, AuthorRole.Editor),       // duplicate (person, role) → one row
            (pohl, AuthorRole.Translator),   // different role → kept
            (asimov, AuthorRole.Author),     // Role=Author in contributors → skipped
        ]);

        Assert.Single(work.WorkAuthors, wa => wa.Author == pohl && wa.Role == AuthorRole.Editor);
        Assert.Single(work.WorkAuthors, wa => wa.Author == pohl && wa.Role == AuthorRole.Translator);
        Assert.Single(work.WorkAuthors, wa => wa.Role == AuthorRole.Author); // just asimov, no dupe
    }
}
