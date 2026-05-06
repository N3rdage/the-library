using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Unit)]
public class GenreCandidateCleanerTests
{
    [Theory]
    [InlineData("Mystery", "Mystery")]
    [InlineData("  detective and mystery stories  ", "Detective and mystery stories")]
    [InlineData("science fiction", "Science fiction")]
    public void Clean_PassesGenuineSubjectsThroughCapitalised(string input, string expected)
    {
        Assert.Equal(expected, GenreCandidateCleaner.Clean(input));
    }

    [Theory]
    // Real subject strings that look like genres but aren't, and previously
    // produced false positives via FuzzyGenreMatch.
    [InlineData("Romance languages")]
    [InlineData("Fiction")]
    [InlineData("English fiction")]
    [InlineData("General")]
    [InlineData("Literature")]
    [InlineData("In English")]
    public void Clean_FiltersDenyListedSubjects(string input)
    {
        Assert.Null(GenreCandidateCleaner.Clean(input));
    }

    [Theory]
    [InlineData("1934")]
    [InlineData("1990s")]
    [InlineData("20th century")]
    [InlineData("19th Century English fiction")]
    public void Clean_FiltersYearsAndCenturies(string input)
    {
        Assert.Null(GenreCandidateCleaner.Clean(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_ReturnsNullForBlankInput(string? input)
    {
        Assert.Null(GenreCandidateCleaner.Clean(input));
    }

    [Fact]
    public void Clean_RejectsExtremelyLongStrings()
    {
        Assert.Null(GenreCandidateCleaner.Clean(new string('x', 81)));
    }
}
