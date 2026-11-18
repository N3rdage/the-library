using BookTracker.Application.Ai;
using BookTracker.Application.Series;
using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests.Services;

// Pins the library text every AI provider puts in its prompts, rendered from
// the read models the providers dispatch for. The reads themselves are
// covered by AiQueryHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class AiPromptContextTests
{
    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    [Fact]
    public void GenreTaxonomy_IndentsChildGenresUnderTheirParentName()
    {
        var text = AiPromptContext.GenreTaxonomy(
        [
            new GenreTaxonomyEntry("Fantasy", null),
            new GenreTaxonomyEntry("Science Fiction", null),
            new GenreTaxonomyEntry("Space Opera", "Science Fiction"),
        ]);

        Assert.Equal("- Fantasy\n- Science Fiction\n  - Space Opera (sub-genre of Science Fiction)", text);
    }

    [Fact]
    public void UnseriesedBooks_ListsTitleAndAuthorPerLine()
    {
        var text = AiPromptContext.UnseriesedBooks(
            [new UnseriesedBook("Good Omens", "Terry Pratchett, Neil Gaiman"), new UnseriesedBook("Stardust", "Neil Gaiman")]);

        Assert.Equal("- \"Good Omens\" by Terry Pratchett, Neil Gaiman\n- \"Stardust\" by Neil Gaiman", text);
    }

    [Fact]
    public void ExistingSeries_ListsNameTypeAndBookCount_OrSaysThereAreNone()
    {
        var text = AiPromptContext.ExistingSeries(
        [
            new SeriesListItem(1, "Discworld", "Terry Pratchett", SeriesType.Series, 3, null),
            new SeriesListItem(2, "Short Stories", null, SeriesType.Collection, 1, null),
        ]);

        Assert.Equal("Existing series/collections:\n- Discworld (Series, 3 books)\n- Short Stories (Collection, 1 books)", text);
        Assert.Equal("No existing series or collections.", AiPromptContext.ExistingSeries([]));
    }

    [Fact]
    public void AdvisorBooks_ListsTitleAuthorRatingAndGenres_OrSaysTheLibraryIsEmpty()
    {
        var text = AiPromptContext.AdvisorBooks(
        [
            new AdvisorBook("A Wizard of Earthsea", "Ursula K. Le Guin", 5, ["Fantasy"]),
            new AdvisorBook("Collected", "Ursula K. Le Guin", 0, ["Fantasy", "Science Fiction"]),
            new AdvisorBook("Untagged", "Anon", 3, []),
        ]);

        Assert.Equal(
            "- \"A Wizard of Earthsea\" by Ursula K. Le Guin | 5/5 | Fantasy\n"
            + "- \"Collected\" by Ursula K. Le Guin | 0/5 | Fantasy, Science Fiction\n"
            + "- \"Untagged\" by Anon | 3/5 | ",
            text);
        Assert.Equal("Library is empty.", AiPromptContext.AdvisorBooks([]));
    }

    [Fact]
    public void LibraryProfile_RendersEverySection()
    {
        var profile = new AiLibraryProfile(
            [new AiAuthorStat("Isaac Asimov", 3, 3.0), new AiAuthorStat("Ursula K. Le Guin", 2, 8.0 / 3)],
            [new AiGenreCount("Science Fiction", 4), new AiGenreCount("Fantasy", 1)],
            [new AiRatedBook("Foundation", "Isaac Asimov", 5), new AiRatedBook("The Dispossessed", "Ursula K. Le Guin", 4)],
            [new AiSeriesGap("Foundation", "Isaac Asimov", 1, 3, [2, 3]), new AiSeriesGap("Zeta", null, 0, 1, [1])]);

        Assert.Equal(
            Lf("""
            Here's the reader's library profile:

            Top authors:
            - Isaac Asimov: 3 books, avg rating 3.0/5
            - Ursula K. Le Guin: 2 books, avg rating 2.7/5

            Top genres:
            - Science Fiction: 4 books
            - Fantasy: 1 books

            Highly rated books (4-5 stars):
            - "Foundation" by Isaac Asimov (5/5)
            - "The Dispossessed" by Ursula K. Le Guin (4/5)

            Incomplete series (gaps to fill):
            - Foundation by Isaac Asimov: have 1/3, missing: 2, 3
            - Zeta by various: have 0/1, missing: 1
            """),
            Lf(AiPromptContext.LibraryProfile(profile)));
    }

    [Fact]
    public void LibraryProfile_ForAnEmptyLibrary_KeepsTheHeadings_AndSaysNoSeriesAreIncomplete()
    {
        Assert.Equal(
            Lf("""
            Here's the reader's library profile:

            Top authors:


            Top genres:


            Highly rated books (4-5 stars):


            Incomplete series (gaps to fill):
            No incomplete series.
            """),
            Lf(AiPromptContext.LibraryProfile(new AiLibraryProfile([], [], [], []))));
    }
}
