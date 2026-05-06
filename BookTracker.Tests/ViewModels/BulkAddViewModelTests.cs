using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class BulkAddViewModelTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private BulkAddViewModel CreateVm() => new(_factory, _lookup, new SeriesMatchService(_factory), NullLogger<BulkAddViewModel>.Instance);

    [Fact]
    public async Task AddIsbnAsync_AddsRowToGrid()
    {
        var vm = CreateVm();
        vm.IsbnInput = "9780345391803";
        vm.OnStateChanged = () => Task.CompletedTask;

        await vm.AddIsbnAsync();

        Assert.Single(vm.Rows);
        Assert.Equal("9780345391803", vm.Rows[0].Isbn);
        Assert.Equal("", vm.IsbnInput); // input is cleared
    }

    [Fact]
    public async Task AddIsbnAsync_AllowsSameIsbnAgain_ToCaptureSecondCopy()
    {
        // Re-scanning the same barcode in one session is meaningful — it's
        // a second physical copy of the book. Each scan creates its own
        // row; SaveBookAsync re-checks the DB at save time and turns the
        // second save into a Copy add rather than colliding on the unique
        // ISBN constraint.
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;

        vm.IsbnInput = "9780345391803";
        await vm.AddIsbnAsync();
        vm.IsbnInput = "9780345391803";
        await vm.AddIsbnAsync();

        Assert.Equal(2, vm.Rows.Count);
    }

    [Fact]
    public async Task AcceptRowAsync_SecondScanOfSameIsbn_AddsCopyInsteadOfDuplicateBook()
    {
        var vm = CreateVm();
        var first = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780345391803",
            Title = "The Hobbit",
            Author = "J.R.R. Tolkien",
            Status = BulkAddViewModel.RowStatus.Found
        };
        var second = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780345391803",
            Title = "The Hobbit",
            Author = "J.R.R. Tolkien",
            Status = BulkAddViewModel.RowStatus.Found
        };

        await vm.AcceptRowAsync(first);
        await vm.AcceptRowAsync(second);

        using var db = _factory.CreateDbContext();
        Assert.Single(db.Books); // Only one Book row…
        Assert.Single(db.Editions); // …and one Edition…
        Assert.Equal(2, db.Copies.Count()); // …with two Copies.
    }

    [Fact]
    public async Task AddIsbnAsync_IgnoresEmptyInput()
    {
        var vm = CreateVm();
        vm.IsbnInput = "  ";

        await vm.AddIsbnAsync();

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task AddIsbnAsync_DetectsDuplicateInDatabase()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(new Book
            {
                Title = "Existing",
                Works = [new Work { Title = "Existing", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Author" }, Order = 0 }] }],
                Editions = [new Edition { Isbn = "9780345391803", Copies = [new Copy { Condition = BookCondition.Good }] }]
            });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;
        vm.IsbnInput = "9780345391803";

        await vm.AddIsbnAsync();

        Assert.True(vm.Rows[0].IsDuplicate);
    }

    [Fact]
    public async Task AddIsbnAsync_ExistingIsbn_HydratesFromLocalAndSkipsUpstreamLookup()
    {
        // Regression: bulk-add was calling LookupByIsbnAsync even for ISBNs
        // already in the library. When upstream providers don't index the
        // ISBN (e.g. older mass-market editions like 055210617X — Funny
        // Money by Richard Sapir, the trigger ISBN that surfaced this bug),
        // the row would render as "Unknown book" despite the book being on
        // a shelf. Local hydration must short-circuit the upstream call.
        using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(new Book
            {
                Title = "Funny Money",
                DefaultCoverArtUrl = "https://example.test/funny-money.jpg",
                Works =
                [
                    new Work
                    {
                        Title = "Funny Money",
                        WorkAuthors = [new WorkAuthor { Author = new Author { Name = "Richard Sapir" }, Order = 0 }]
                    }
                ],
                Editions =
                [
                    new Edition
                    {
                        Isbn = "055210617X",
                        Format = BookFormat.MassMarketPaperback,
                        Publisher = new Publisher { Name = "Pinnacle" },
                        Copies = [new Copy { Condition = BookCondition.Good }]
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;
        vm.IsbnInput = "055210617X";

        await vm.AddIsbnAsync();

        var row = Assert.Single(vm.Rows);
        Assert.True(row.IsDuplicate);
        Assert.Equal(BulkAddViewModel.RowStatus.Found, row.Status);
        Assert.Equal("Funny Money", row.Title);
        Assert.Equal("Richard Sapir", row.Author);
        Assert.Equal("Pinnacle", row.Publisher);
        Assert.Equal(BookFormat.MassMarketPaperback, row.Format);
        Assert.Equal("Local library", row.Source);
        Assert.Equal("https://example.test/funny-money.jpg", row.CoverUrl);

        await _lookup.DidNotReceive().LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RemoveRow_RemovesFromGrid()
    {
        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow { Isbn = "123" };
        vm.Rows.Add(row);

        vm.RemoveRow(row);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task AcceptRowAsync_CreatesBookAndCopy()
    {
        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780345391803",
            Title = "The Hobbit",
            Author = "J.R.R. Tolkien",
            Status = BulkAddViewModel.RowStatus.Found
        };

        await vm.AcceptRowAsync(row);

        Assert.Equal(BulkAddViewModel.RowAction.Accepted, row.Action);

        using var db = _factory.CreateDbContext();
        var book = db.Books.Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author).FirstOrDefault(b => b.Title == "The Hobbit");
        Assert.NotNull(book);
        Assert.Equal("J.R.R. Tolkien", book.Works.Single().WorkAuthors.OrderBy(wa => wa.Order).First().Author.Name);
    }

    [Fact]
    public async Task AcceptRowAsync_CreatesWorkAlongsideBook()
    {
        // After the Work cutover the bulk-add flow saves a Book and a
        // single Work in one go. The Work carries the author and any
        // genres derived from the lookup.
        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780345391803",
            Title = "The Hobbit",
            Author = "J.R.R. Tolkien",
            Status = BulkAddViewModel.RowStatus.Found
        };

        await vm.AcceptRowAsync(row);

        using var db = _factory.CreateDbContext();
        var book = db.Books.Include(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author).Single(b => b.Title == "The Hobbit");
        var work = Assert.Single(book.Works);
        Assert.Equal("The Hobbit", work.Title);
        Assert.Equal("J.R.R. Tolkien", work.WorkAuthors.OrderBy(wa => wa.Order).First().Author.Name);
    }

    [Fact]
    public async Task FollowUpRowAsync_CreatesBookWithFollowUpTag()
    {
        // Seed the follow-up tag (like the real migration does)
        using (var db = _factory.CreateDbContext())
        {
            db.Tags.Add(new Tag { Name = "follow-up" });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780345391803",
            Title = "The Hobbit",
            Author = "J.R.R. Tolkien",
            Status = BulkAddViewModel.RowStatus.Found
        };

        await vm.FollowUpRowAsync(row);

        Assert.Equal(BulkAddViewModel.RowAction.FollowUp, row.Action);

        using var db2 = _factory.CreateDbContext();
        var book = db2.Books.FirstOrDefault(b => b.Title == "The Hobbit");
        Assert.NotNull(book);
    }

    [Fact]
    public async Task FollowUpNotFoundAsync_UsesDefaultTitle()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Tags.Add(new Tag { Name = "follow-up" });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "1234567890",
            Status = BulkAddViewModel.RowStatus.NotFound
        };

        await vm.FollowUpNotFoundAsync(row);

        Assert.Equal(BulkAddViewModel.RowAction.FollowUp, row.Action);
        Assert.StartsWith("Unknown book", row.Title);
    }

    [Fact]
    public async Task AcceptAllFoundAsync_AcceptsOnlyFoundNonDuplicates()
    {
        var vm = CreateVm();
        var found = new BulkAddViewModel.DiscoveryRow { Isbn = "111", Title = "A", Author = "A", Status = BulkAddViewModel.RowStatus.Found };
        var duplicate = new BulkAddViewModel.DiscoveryRow { Isbn = "222", Title = "B", Author = "B", Status = BulkAddViewModel.RowStatus.Found, IsDuplicate = true };
        var notFound = new BulkAddViewModel.DiscoveryRow { Isbn = "333", Status = BulkAddViewModel.RowStatus.NotFound };

        vm.Rows.AddRange([found, duplicate, notFound]);

        await vm.AcceptAllFoundAsync();

        Assert.Equal(BulkAddViewModel.RowAction.Accepted, found.Action);
        Assert.Equal(BulkAddViewModel.RowAction.Pending, duplicate.Action); // skipped
        Assert.Equal(BulkAddViewModel.RowAction.Pending, notFound.Action); // skipped
    }

    [Theory]
    [InlineData(BulkAddViewModel.RowAction.Accepted, "table-success")]
    [InlineData(BulkAddViewModel.RowAction.FollowUp, "table-warning")]
    [InlineData(BulkAddViewModel.RowAction.Duplicate, "table-secondary")]
    [InlineData(BulkAddViewModel.RowAction.Pending, "")]
    public void RowCssClass_ReturnsCorrectClass(BulkAddViewModel.RowAction action, string expected)
    {
        var row = new BulkAddViewModel.DiscoveryRow { Action = action };
        Assert.Equal(expected, BulkAddViewModel.RowCssClass(row));
    }

    [Fact]
    public async Task AcceptRowAsync_WithAcceptedNewSeries_FindOrCreatesSeriesAndAttaches()
    {
        // Per-row analogue of the BookAddViewModel test: accepting an
        // ApiMatchNewSeries suggestion in bulk capture should create the
        // Series row on save and attach the new Work to it.
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;

        // Construct the row directly to avoid the async lookup-pipeline
        // setup. AcceptSeriesSuggestion + SaveBookAsync don't depend on how
        // the row got its SeriesSuggestion populated.
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780765326355",
            Title = "The Way of Kings",
            Author = "Brandon Sanderson",
            Status = BulkAddViewModel.RowStatus.Found,
            SeriesSuggestion = new SeriesMatch(
                SeriesId: null,
                SeriesName: "The Stormlight Archive",
                SeriesType: null,
                Reason: MatchReason.ApiMatchNewSeries,
                Message: "Open Library suggests this is part of \"The Stormlight Archive\" #1 — accept to create the series and attach this book.",
                SuggestedOrder: 1)
        };
        vm.Rows.Add(row);

        vm.AcceptSeriesSuggestion(row);
        Assert.True(row.SeriesSuggestionAccepted);

        await vm.AcceptRowAsync(row);

        using var db = _factory.CreateDbContext();
        var series = Assert.Single(db.Series);
        Assert.Equal("The Stormlight Archive", series.Name);
        Assert.Equal(SeriesType.Series, series.Type);

        var work = db.Works.Include(w => w.Series).Single();
        Assert.Equal(series.Id, work.SeriesId);
        Assert.Equal(1, work.SeriesOrder);
    }

    [Fact]
    public async Task AcceptRowAsync_WithAcceptedExistingSeries_AttachesBySeriesId()
    {
        int seededSeriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Discworld", Type = SeriesType.Series };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seededSeriesId = series.Id;
        }

        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;
        var row = new BulkAddViewModel.DiscoveryRow
        {
            Isbn = "9780552134613",
            Title = "Sourcery",
            Author = "Terry Pratchett",
            Status = BulkAddViewModel.RowStatus.Found,
            SeriesSuggestion = new SeriesMatch(
                SeriesId: seededSeriesId,
                SeriesName: "Discworld",
                SeriesType: SeriesType.Series,
                Reason: MatchReason.ApiMatchExisting,
                Message: "Open Library indicates this is part of \"Discworld\" #5",
                SuggestedOrder: 5)
        };
        vm.Rows.Add(row);

        vm.AcceptSeriesSuggestion(row);
        await vm.AcceptRowAsync(row);

        using var db2 = _factory.CreateDbContext();
        var work = db2.Works.Include(w => w.Series).Single();
        Assert.Equal(seededSeriesId, work.SeriesId);
        Assert.Equal(5, work.SeriesOrder);
        // No new Series row created — attached to the seeded one.
        Assert.Equal(1, db2.Series.Count());
    }
}
