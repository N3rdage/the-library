using BookTracker.Data;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Pure domain unit tests for the Author aggregate — no EF, no container. Covers
// the Create factory, Rename, and the alias pointer (MarkAliasOf /
// PromoteToCanonical). Name uniqueness and the alias-graph re-rooting are
// cross-row rules tested on their handlers.
[Trait("Category", TestCategories.Unit)]
public class AuthorAggregateTests
{
    [Fact]
    public void Create_trimsName_andIsCanonical()
    {
        var author = Author.Create("  Ursula K. Le Guin  ");

        Assert.Equal("Ursula K. Le Guin", author.Name);
        Assert.Null(author.CanonicalAuthorId);
        Assert.Null(author.CanonicalAuthor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_blankName_throws(string name)
    {
        Assert.Throws<DomainRuleException>(() => Author.Create(name));
    }

    [Fact]
    public void Rename_trimsName()
    {
        var author = Author.Create("Steve King");

        author.Rename("  Stephen King  ");

        Assert.Equal("Stephen King", author.Name);
    }

    [Fact]
    public void Rename_blankName_throws_andKeepsTheOldName()
    {
        var author = Author.Create("Stephen King");

        Assert.Throws<DomainRuleException>(() => author.Rename("  "));
        Assert.Equal("Stephen King", author.Name);
    }

    [Fact]
    public void MarkAliasOf_pointsKeyAndNavigationAtTheCanonical()
    {
        var king = Author.Create("Stephen King");
        king.Id = 7;
        var bachman = Author.Create("Richard Bachman");

        bachman.MarkAliasOf(king);

        Assert.Equal(7, bachman.CanonicalAuthorId);
        Assert.Same(king, bachman.CanonicalAuthor);
    }

    [Fact]
    public void MarkAliasOf_aTargetThatIsItselfAnAlias_isTakenAsGiven()
    {
        // Re-rooting onto the target's canonical is MarkAuthorAsAliasOf's job;
        // the entity points exactly where it is told.
        var king = Author.Create("Stephen King");
        king.Id = 1;
        var bachman = Author.Create("Richard Bachman");
        bachman.Id = 2;
        bachman.MarkAliasOf(king);
        var other = Author.Create("Another Pen Name");

        other.MarkAliasOf(bachman);

        Assert.Equal(2, other.CanonicalAuthorId);
        Assert.Same(bachman, other.CanonicalAuthor);
    }

    [Fact]
    public void MarkAliasOf_anUnsavedCanonical_setsTheNavigationAndLeavesTheKeyNull()
    {
        // EF fills the key from the navigation on insert; a 0 would be a real
        // (wrong) key value.
        var king = Author.Create("Stephen King");
        var bachman = Author.Create("Richard Bachman");

        bachman.MarkAliasOf(king);

        Assert.Null(bachman.CanonicalAuthorId);
        Assert.Same(king, bachman.CanonicalAuthor);
    }

    [Fact]
    public void PromoteToCanonical_clearsKeyAndNavigation()
    {
        var king = Author.Create("Stephen King");
        king.Id = 7;
        var bachman = Author.Create("Richard Bachman");
        bachman.MarkAliasOf(king);

        bachman.PromoteToCanonical();

        Assert.Null(bachman.CanonicalAuthorId);
        Assert.Null(bachman.CanonicalAuthor);
    }
}
