using BookTracker.Data.Models;
using BookTracker.Web.Services;

namespace BookTracker.Tests;

// Pins the library-profile text the AI providers put in the shopping-list
// prompt for a seeded library: top authors with credit count and average
// rating, top genres, highly rated books, and the series gaps.
[Trait("Category", TestCategories.Integration)]
public class AiLibraryContextTests
{
    private readonly TestDbContextFactory _factory = new();

    private async Task<string> LibraryContext()
    {
        using var db = _factory.CreateDbContext();
        return await SharedParsers.BuildLibraryContextAsync(db, CancellationToken.None);
    }

    [Fact]
    public async Task SeededLibrary_RendersEveryProfileSection()
    {
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var leGuin = new Author { Name = "Ursula K. Le Guin" };
            var sf = new Genre { Name = "Science Fiction" };
            var fantasy = new Genre { Name = "Fantasy" };
            db.Genres.Add(new Genre { Name = "Poetry" });   // unused: not a top genre

            // Inserted in name order so the gap lines read the same whatever
            // order the series come back in.
            var foundation = new Series { Name = "Foundation", Author = "Isaac Asimov", Type = SeriesType.Series, ExpectedCount = 3 };
            var zeta = new Series { Name = "Zeta", Type = SeriesType.Series, ExpectedCount = 1 };
            var open = new Series { Name = "Open Ended", Type = SeriesType.Series };
            db.Series.AddRange(foundation, zeta, open);

            db.Books.AddRange(
                new Book
                {
                    Title = "Foundation", Rating = 5, Series = foundation, SeriesOrder = 1,
                    Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }], Genres = [sf] }],
                },
                new Book
                {
                    Title = "I, Robot", Rating = 3,
                    Works = [new Work { Title = "I, Robot", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }], Genres = [sf] }],
                },
                new Book
                {
                    Title = "A Wizard of Earthsea", Rating = 4, Series = open, SeriesOrder = 1,
                    Works = [new Work { Title = "A Wizard of Earthsea", WorkAuthors = [new WorkAuthor { Author = leGuin, Order = 0 }], Genres = [fantasy] }],
                });
            await db.SaveChangesAsync();
        }

        var text = await LibraryContext();

        Assert.Equal(
            """
            Here's the reader's library profile:

            Top authors:
            - Isaac Asimov: 2 books, avg rating 4.0/5
            - Ursula K. Le Guin: 1 books, avg rating 4.0/5

            Top genres:
            - Science Fiction: 2 books
            - Fantasy: 1 books

            Highly rated books (4-5 stars):
            - "Foundation" by Isaac Asimov (5/5)
            - "A Wizard of Earthsea" by Ursula K. Le Guin (4/5)

            Incomplete series (gaps to fill):
            - Foundation by Isaac Asimov: have 1/3, missing: 2, 3
            - Zeta by various: have 0/1, missing: 1
            """.ReplaceLineEndings("\n"),
            text.ReplaceLineEndings("\n"));
    }
}
