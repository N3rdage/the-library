using BookTracker.Data.Models;
using BookTracker.Web.Services.Catalog;

namespace BookTracker.Tests.Services;

[Trait("Category", TestCategories.Integration)]
public class CatalogSnapshotServiceTests
{
    private readonly TestDbContextFactory _factory = new();

    private CatalogSnapshotService CreateService() => new(_factory);

    [Fact]
    public async Task GetSnapshotAsync_PrimaryAndAllAuthors_OnSingleWorkBook()
    {
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            db.Authors.Add(asimov);
            db.Books.Add(new Book
            {
                Title = "Foundation",
                Works = [new Work { Title = "Foundation", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] }]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();

        var book = Assert.Single(snapshot.Books);
        Assert.Equal("Foundation", book.Title);
        Assert.Equal("Isaac Asimov", book.PrimaryAuthor);
        Assert.Equal(["Isaac Asimov"], book.AllAuthors);
    }

    [Fact]
    public async Task GetSnapshotAsync_PrimaryAndAllAuthors_OnMultiWorkCompendium()
    {
        // Compendium with three Works, each by a different author. Primary
        // author = first Work's lowest-Order WorkAuthor; AllAuthors lists
        // every credited contributor.
        using (var db = _factory.CreateDbContext())
        {
            var asimov = new Author { Name = "Isaac Asimov" };
            var king = new Author { Name = "Stephen King" };
            var bradbury = new Author { Name = "Ray Bradbury" };
            db.Authors.AddRange(asimov, king, bradbury);
            db.Books.Add(new Book
            {
                Title = "The Funhouse",
                Works =
                [
                    new Work { Title = "Asimov story", WorkAuthors = [new WorkAuthor { Author = asimov, Order = 0 }] },
                    new Work { Title = "King story", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] },
                    new Work { Title = "Bradbury story", WorkAuthors = [new WorkAuthor { Author = bradbury, Order = 0 }] },
                ]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();

        var book = Assert.Single(snapshot.Books);
        Assert.Equal("Isaac Asimov", book.PrimaryAuthor);
        Assert.Equal(["Isaac Asimov", "Stephen King", "Ray Bradbury"], book.AllAuthors);
    }

    [Fact]
    public async Task GetSnapshotAsync_AliasBookCounts_RolledUpAtCanonical_NotAtAlias()
    {
        // King canonical, Bachman alias. Carrie credited to King, The Long
        // Walk credited to Bachman. Canonical row should show the rolled-up
        // count (2); alias row shows just its direct count (1).
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            db.Authors.Add(king);
            await db.SaveChangesAsync();

            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = king.Id };
            db.Authors.Add(bachman);
            db.Books.AddRange(
                new Book { Title = "Carrie", Works = [new Work { Title = "Carrie", WorkAuthors = [new WorkAuthor { Author = king, Order = 0 }] }] },
                new Book { Title = "The Long Walk", Works = [new Work { Title = "The Long Walk", WorkAuthors = [new WorkAuthor { Author = bachman, Order = 0 }] }] });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();

        var kingRow = snapshot.Authors.Single(a => a.Name == "Stephen King");
        var bachmanRow = snapshot.Authors.Single(a => a.Name == "Richard Bachman");

        Assert.Equal(kingRow.Id, kingRow.CanonicalId);
        Assert.Equal(2, kingRow.BookCount);

        Assert.Equal(kingRow.Id, bachmanRow.CanonicalId);
        Assert.Equal(1, bachmanRow.BookCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_BookCreditedToBothCanonicalAndAlias_NotDoubleCountedInRollup()
    {
        // Edge case: a single book has WorkAuthors crediting both King AND
        // Bachman (rare but possible — e.g. a foreword/author note that
        // names the alias). Canonical rollup must not double-count.
        using (var db = _factory.CreateDbContext())
        {
            var king = new Author { Name = "Stephen King" };
            db.Authors.Add(king);
            await db.SaveChangesAsync();

            var bachman = new Author { Name = "Richard Bachman", CanonicalAuthorId = king.Id };
            db.Authors.Add(bachman);
            db.Books.Add(new Book
            {
                Title = "The Bachman Books",
                Works = [new Work
                {
                    Title = "The Bachman Books",
                    WorkAuthors =
                    [
                        new WorkAuthor { Author = king, Order = 0 },
                        new WorkAuthor { Author = bachman, Order = 1 },
                    ]
                }]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var kingRow = snapshot.Authors.Single(a => a.Name == "Stephen King");

        // Despite the book being credited to both King AND Bachman (whose
        // canonical is King), the rollup should count it once.
        Assert.Equal(1, kingRow.BookCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_CollectsIsbnsAcrossEditions()
    {
        // A Book with two Editions, different ISBNs — both should appear
        // in BookSnapshot.Isbns. Empty / null ISBNs are filtered out.
        using (var db = _factory.CreateDbContext())
        {
            var clarke = new Author { Name = "Arthur C. Clarke" };
            db.Authors.Add(clarke);
            db.Books.Add(new Book
            {
                Title = "Rendezvous with Rama",
                Works = [new Work { Title = "Rendezvous with Rama", WorkAuthors = [new WorkAuthor { Author = clarke, Order = 0 }] }],
                Editions =
                [
                    new Edition { Isbn = "9780553287899", Format = BookFormat.MassMarketPaperback },
                    new Edition { Isbn = "9780575094192", Format = BookFormat.TradePaperback },
                    new Edition { Isbn = null, Format = BookFormat.Hardcover },
                ]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var book = Assert.Single(snapshot.Books);

        Assert.Equal(2, book.Isbns.Count);
        Assert.Contains("9780553287899", book.Isbns);
        Assert.Contains("9780575094192", book.Isbns);
    }

    [Fact]
    public async Task GetSnapshotAsync_VersionAndSyncedAtPopulated()
    {
        // Catalog version is the deployed commit SHA (or "dev" locally)
        // so the SW can detect a deploy and invalidate cached snapshots.
        // SyncedAt is server clock at projection time.
        var before = DateTime.UtcNow.AddSeconds(-5);
        var snapshot = await CreateService().GetSnapshotAsync();
        var after = DateTime.UtcNow.AddSeconds(5);

        Assert.False(string.IsNullOrWhiteSpace(snapshot.Version));
        Assert.InRange(snapshot.SyncedAt, before, after);
    }

    [Fact]
    public async Task GetSnapshotAsync_BookStatusAndRatingProjected()
    {
        // Status + rating need to make the trip — the bookshop result
        // card displays both.
        using (var db = _factory.CreateDbContext())
        {
            var clarke = new Author { Name = "Arthur C. Clarke" };
            db.Authors.Add(clarke);
            db.Books.Add(new Book
            {
                Title = "2001: A Space Odyssey",
                Status = BookStatus.Read,
                Rating = 5,
                Works = [new Work { Title = "2001", WorkAuthors = [new WorkAuthor { Author = clarke, Order = 0 }] }]
            });
            await db.SaveChangesAsync();
        }

        var snapshot = await CreateService().GetSnapshotAsync();
        var book = Assert.Single(snapshot.Books);

        Assert.Equal(BookStatus.Read, book.Status);
        Assert.Equal(5, book.Rating);
    }
}
