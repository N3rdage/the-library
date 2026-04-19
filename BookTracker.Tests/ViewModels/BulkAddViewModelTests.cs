using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

public class BulkAddViewModelTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private BulkAddViewModel CreateVm() => new(_factory, _lookup, new SeriesMatchService(_factory));

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
    public async Task AddIsbnAsync_IgnoresDuplicateIsbnInGrid()
    {
        var vm = CreateVm();
        vm.OnStateChanged = () => Task.CompletedTask;

        vm.IsbnInput = "9780345391803";
        await vm.AddIsbnAsync();
        vm.IsbnInput = "9780345391803";
        await vm.AddIsbnAsync();

        Assert.Single(vm.Rows);
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
                Author = "Author",
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
        var book = db.Books.FirstOrDefault(b => b.Title == "The Hobbit");
        Assert.NotNull(book);
        Assert.Equal("J.R.R. Tolkien", book.Author);
    }

    [Fact]
    public async Task AcceptRowAsync_DualWritesAMirroringWork()
    {
        // PR 1 of the Work refactor: every saved Book must have exactly one
        // mirroring Work via WorkSync.EnsureWork. This test guards the
        // dual-write at the BulkAdd save site.
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
        var book = db.Books.Include(b => b.Works).Single(b => b.Title == "The Hobbit");
        var work = Assert.Single(book.Works);
        Assert.Equal("The Hobbit", work.Title);
        Assert.Equal("J.R.R. Tolkien", work.Author);
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
}
