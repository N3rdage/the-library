using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;

namespace BookTracker.Mobile.Cache.Tests;

[Trait("Category", "Unit")]
public class ContributorFormatterTests
{
    [Fact]
    public void Format_EmptyOrNull_ReturnsEmptyString()
    {
        Assert.Equal("", ContributorFormatter.Format(null));
        Assert.Equal("", ContributorFormatter.Format([]));
    }

    [Fact]
    public void Format_SingleAuthor_NameOnly()
    {
        Assert.Equal("Tolkien",
            ContributorFormatter.Format([new("Tolkien", "Author")]));
    }

    [Fact]
    public void Format_TwoAuthors_AmpersandJoin()
    {
        Assert.Equal("Preston & Child",
            ContributorFormatter.Format([new("Preston", "Author"), new("Child", "Author")]));
    }

    [Fact]
    public void Format_ThreePlusAuthors_CommaJoin()
    {
        Assert.Equal("Asimov, Bradbury, Clarke",
            ContributorFormatter.Format([
                new("Asimov", "Author"), new("Bradbury", "Author"), new("Clarke", "Author"),
            ]));
    }

    [Fact]
    public void Format_EditorOnly_SingleNonAuthorWithRoleSuffix()
    {
        // Dictionary / Oxford Companion case — no Author, just an
        // Editor. By-line should read "Mauss (editor)" not "(unknown)".
        Assert.Equal("Doug Mauss (editor)",
            ContributorFormatter.Format([new("Doug Mauss", "Editor")]));
    }

    [Fact]
    public void Format_AuthorPlusIllustrator_AuthorThenSuffix()
    {
        // The Hobbit example — Tolkien wrote it, Cariello illustrated.
        // Mirrors the Web side's "Tolkien; Sergio Cariello (illustrator)".
        Assert.Equal("Tolkien; Sergio Cariello (illustrator)",
            ContributorFormatter.Format([
                new("Tolkien", "Author"),
                new("Sergio Cariello", "Illustrator"),
            ]));
    }

    [Fact]
    public void Format_AuthorPlusTwoNonAuthorRoles_AllSemicolonJoined()
    {
        Assert.Equal("Tolkien; Doug Mauss (editor); Sergio Cariello (illustrator)",
            ContributorFormatter.Format([
                new("Tolkien", "Author"),
                new("Doug Mauss", "Editor"),
                new("Sergio Cariello", "Illustrator"),
            ]));
    }

    [Fact]
    public void Format_IgnoresBlankNames()
    {
        // Defensive: a malformed snapshot row with an empty Name
        // shouldn't crash or produce a stray "(editor)" with no
        // person attached.
        Assert.Equal("Tolkien",
            ContributorFormatter.Format([
                new("Tolkien", "Author"),
                new("", "Editor"),
                new("   ", "Translator"),
            ]));
    }
}
