using BookTracker.Application.Publishers;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the /publishers read-model handlers. Relocated from
// PublisherListViewModelTests when the DbContext reads moved into
// GetPublisherList / GetPublisherEditions (PR6b-2).
[Trait("Category", TestCategories.Integration)]
public class GetPublisherListHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    [Fact]
    public async Task GetPublisherList_PopulatesRows_WithEditionCounts()
    {
        using (var db = _factory.CreateDbContext())
        {
            var tor = new Publisher { Name = "Tor" };
            var unused = new Publisher { Name = "Unused Press" };
            db.Publishers.AddRange(tor, unused);
            db.Books.Add(new Book
            {
                Title = "Foo",
                Editions =
                [
                    new Edition { Publisher = tor, Format = BookFormat.Hardcover },
                    new Edition { Publisher = tor, Format = BookFormat.TradePaperback }
                ]
            });
            await db.SaveChangesAsync();
        }

        var rows = await new GetPublisherListHandler(_factory).HandleAsync(new GetPublisherList());

        Assert.Equal(2, rows.Count);
        Assert.Equal("Tor", rows[0].Name);           // ordered by name
        Assert.Equal(2, rows.Single(p => p.Name == "Tor").EditionCount);
        Assert.Equal(0, rows.Single(p => p.Name == "Unused Press").EditionCount);
    }

    [Fact]
    public async Task GetPublisherEditions_ReturnsEditionsForPublisher_OrderedByBookTitle()
    {
        int torId;
        using (var db = _factory.CreateDbContext())
        {
            var tor = new Publisher { Name = "Tor" };
            db.Publishers.Add(tor);
            db.Books.AddRange(
                new Book { Title = "Zebra", Editions = [new Edition { Publisher = tor, Format = BookFormat.Hardcover, Copies = [new Copy()] }] },
                new Book { Title = "Apple", Editions = [new Edition { Publisher = tor, Format = BookFormat.TradePaperback }] });
            await db.SaveChangesAsync();
            torId = tor.Id;
        }

        var detail = await new GetPublisherEditionsHandler(_factory).HandleAsync(new GetPublisherEditions(torId));

        Assert.Equal(2, detail.Editions.Count);
        Assert.Equal("Apple", detail.Editions[0].BookTitle); // ordered by book title
        Assert.Equal("Zebra", detail.Editions[1].BookTitle);
        Assert.Equal(1, detail.Editions.Single(e => e.BookTitle == "Zebra").CopyCount);
    }
}
