using BookTracker.Application.Ai;
using BookTracker.Application.Wishlist;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the AI prompts' library reads: the genre taxonomy, the
// unseriesed books, the advisor's whole-library list and the shopping-list
// profile. The text each one becomes is covered by AiPromptContextTests.
[Trait("Category", TestCategories.Integration)]
public class AiQueryHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private static Book NewBook(string title, Author author, int rating = 0, Series? series = null, int? order = null, params Genre[] genres) => new()
    {
        Title = title,
        Rating = rating,
        Series = series,
        SeriesOrder = order,
        Works = [new Work { Title = title, WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }], Genres = [.. genres] }],
    };

    [Fact]
    public async Task GenreTaxonomy_ListsEveryGenreByName_WithItsParentName()
    {
        using (var db = _factory.CreateDbContext())
        {
            var sf = new Genre { Name = "Science Fiction" };
            db.Genres.AddRange(
                new Genre { Name = "Space Opera", ParentGenre = sf },
                sf,
                new Genre { Name = "Fantasy" });
            await db.SaveChangesAsync();
        }

        var taxonomy = await new GetGenreTaxonomyHandler(_factory).HandleAsync(new GetGenreTaxonomy());

        Assert.Equal(
            new[]
            {
                new GenreTaxonomyEntry("Fantasy", null),
                new GenreTaxonomyEntry("Science Fiction", null),
                new GenreTaxonomyEntry("Space Opera", "Science Fiction"),
            },
            taxonomy);
    }

    [Fact]
    public async Task UnseriesedBooks_AreTheLiveBooksInNoSeries_ByTitle_WithJoinedAuthors()
    {
        using (var db = _factory.CreateDbContext())
        {
            var gaiman = new Author { Name = "Neil Gaiman" };
            var pratchett = new Author { Name = "Terry Pratchett" };
            var series = new Series { Name = "Discworld", Type = SeriesType.Series };
            var omens = new Book
            {
                Title = "Good Omens",
                Works = [new Work { Title = "Good Omens", WorkAuthors = [new WorkAuthor { Author = pratchett, Order = 0 }, new WorkAuthor { Author = gaiman, Order = 1 }] }],
            };
            var deleted = NewBook("Tombstoned", gaiman);
            deleted.DeletedAt = DateTime.UtcNow;
            db.Books.AddRange(
                NewBook("Stardust", gaiman),
                omens,
                NewBook("Mort", pratchett, series: series, order: 4),
                deleted);
            await db.SaveChangesAsync();
        }

        var books = await new GetUnseriesedBooksForAiHandler(_factory).HandleAsync(new GetUnseriesedBooksForAi());

        Assert.Equal(new[] { "Good Omens", "Stardust" }, books.Select(b => b.Title));
        Assert.Equal("Neil Gaiman", books[1].Author);
        // Two credited authors, joined; the join order is the database's.
        Assert.Equal(
            new[] { "Neil Gaiman", "Terry Pratchett" },
            books[0].Author.Split(", ").Order());
    }

    [Fact]
    public async Task AdvisorProfile_IsEveryLiveBookByTitle_WithRating_AndDistinctGenresAcrossWorks()
    {
        using (var db = _factory.CreateDbContext())
        {
            var author = new Author { Name = "Ursula K. Le Guin" };
            var fantasy = new Genre { Name = "Fantasy" };
            var sf = new Genre { Name = "Science Fiction" };
            var compendium = new Book
            {
                Title = "Collected",
                Rating = 4,
                Works =
                [
                    new Work { Title = "One", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }], Genres = [fantasy] },
                    new Work { Title = "Two", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }], Genres = [fantasy, sf] },
                ],
            };
            var deleted = NewBook("Tombstoned", author, 5);
            deleted.DeletedAt = DateTime.UtcNow;
            db.Books.AddRange(compendium, NewBook("A Wizard of Earthsea", author, 5, genres: fantasy), deleted);
            await db.SaveChangesAsync();
        }

        var books = await new GetLibraryProfileForAdvisorHandler(_factory).HandleAsync(new GetLibraryProfileForAdvisor());

        Assert.Equal(new[] { "A Wizard of Earthsea", "Collected" }, books.Select(b => b.Title));
        Assert.Equal(new[] { 5, 4 }, books.Select(b => b.Rating));
        Assert.Equal("Ursula K. Le Guin", books[1].Author);   // one author across both works
        Assert.Equal(new[] { "Fantasy", "Science Fiction" }, books[1].Genres.Order());
    }

    [Fact]
    public async Task LibraryProfile_RanksAuthorsAndRatedBooks_AndTakesItsGapsFromGetSeriesGaps()
    {
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var leGuin = new Author { Name = "Ursula K. Le Guin" };
            // Added out of name order: the gaps come back in GetSeriesGaps' name order.
            var zeta = new Series { Name = "Zeta", Type = SeriesType.Series, ExpectedCount = 2 };
            var foundation = new Series { Name = "Foundation", Author = "Isaac Asimov", Type = SeriesType.Series, ExpectedCount = 3 };
            db.Series.AddRange(zeta, foundation);
            var interquel = NewBook("Interlude", asimov, 2, foundation, 2);
            interquel.SeriesOrderDisplay = "2.5";
            db.Books.AddRange(
                NewBook("Foundation", asimov, 5, foundation, 1),
                interquel,
                NewBook("I, Robot", asimov, 2),
                NewBook("The Dispossessed", leGuin, 4),
                NewBook("The Lathe of Heaven", leGuin, 4),
                NewBook("Unrated", leGuin, 0));
            await db.SaveChangesAsync();
        }

        var profile = await new GetAiLibraryProfileHandler(_factory, new GetSeriesGapsHandler(_factory))
            .HandleAsync(new GetAiLibraryProfile());

        // Le Guin and Asimov have three works each; the order between the two is
        // not fixed, so compare by name.
        Assert.Equal(2, profile.TopAuthors.Count);
        var asimovStat = Assert.Single(profile.TopAuthors, a => a.Author == "Isaac Asimov");
        Assert.Equal(3, asimovStat.Count);
        Assert.Equal(3.0, asimovStat.AvgRating, 3);              // (5 + 2 + 2) / 3
        var leGuinStat = Assert.Single(profile.TopAuthors, a => a.Author == "Ursula K. Le Guin");
        Assert.Equal(8.0 / 3, leGuinStat.AvgRating, 3);          // (4 + 4 + 0) / 3

        Assert.Equal(
            new[] { "Foundation", "The Dispossessed", "The Lathe of Heaven" },
            profile.HighlyRated.Select(b => b.Title));         // rating desc, then title
        Assert.Equal(new[] { 5, 4, 4 }, profile.HighlyRated.Select(b => b.Rating));
        Assert.Equal("Isaac Asimov", profile.HighlyRated[0].Author);

        Assert.Equal(new[] { "Foundation", "Zeta" }, profile.SeriesGaps.Select(g => g.SeriesName));
        var gap = profile.SeriesGaps[0];
        Assert.Equal("Isaac Asimov", gap.Author);
        Assert.Equal(1, gap.OwnedCount);                        // the 2.5 interquel owns no slot
        Assert.Equal(3, gap.ExpectedCount);
        Assert.Equal(new[] { 2, 3 }, gap.MissingPositions);
        Assert.Null(profile.SeriesGaps[1].Author);
        Assert.Equal(new[] { 1, 2 }, profile.SeriesGaps[1].MissingPositions);
    }
}
