using BookTracker.Application.Shelf;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

[Trait("Category", TestCategories.Integration)]
public class GetShelfOrderHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task GroupsByRootGenre_UnsortedLast_AndOrdersSeriesThenTitle()
    {
        using (var db = _factory.CreateDbContext())
        {
            var fiction = new Genre { Name = "GSO Fiction" };
            var fantasy = new Genre { Name = "GSO Comic Fantasy", ParentGenre = fiction };
            var pratchett = Author.Create("Terry Pratchett");
            var discworld = Series.Create("Discworld", null, SeriesType.Series, null, null);
            var anotherSeries = Series.Create("Long Earth", null, SeriesType.Series, null, null);

            Book Shelve(string title, Series series, int? order)
            {
                var book = Book.Create(title, BookCategory.Fiction, BookStatus.Unread, 0, null, null);
                book.AssignToSeries(series, order, null);
                var work = Work.Create(book, title, null, null, DatePrecision.Day, [pratchett]);
                work.SetGenres([fantasy]);
                return book;
            }

            var unsorted = Book.Create("Loose Notes", BookCategory.Fiction, BookStatus.Unread, 0, null, null);
            Work.Create(unsorted, "Loose Notes", null, null, DatePrecision.Day, [Author.Create("Nobody Special")]);

            db.Books.AddRange(
                Shelve("The Long Earth", anotherSeries, 1),
                // Same series, no order: the title decides.
                Shelve("The Colour of Magic", discworld, null),
                Shelve("Mort", discworld, null),
                unsorted);
            await db.SaveChangesAsync();
        }

        var view = await new GetShelfOrderHandler(_factory).HandleAsync(new GetShelfOrder());

        Assert.Equal(4, view.TotalBooks);
        Assert.Equal(["GSO Fiction", GetShelfOrderHandler.UnsortedGroup], view.Groups.Select(g => g.Genre).ToList());
        Assert.Equal(
            ["Mort", "The Colour of Magic", "The Long Earth"],
            view.Groups[0].Books.Select(b => b.Title).ToList());
        Assert.Equal("Pratchett, Terry", view.Groups[0].Books[0].ShelvedAs);
        Assert.Equal("Discworld", view.Groups[0].Books[0].SeriesName);
        Assert.Equal("Loose Notes", Assert.Single(view.Groups[1].Books).Title);
    }
}
