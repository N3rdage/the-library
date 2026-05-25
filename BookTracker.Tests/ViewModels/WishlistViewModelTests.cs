using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Integration)]
public class WishlistViewModelTests
{
    private readonly TestDbContextFactory _factory = new();
    private readonly IBookLookupService _lookup = Substitute.For<IBookLookupService>();

    private WishlistViewModel CreateVm() =>
        new(_factory, _lookup, NullLogger<WishlistViewModel>.Instance);

    [Fact]
    public async Task SearchAsync_IsbnShapedQuery_CallsIsbnLookupAndPopulatesSingleCandidate()
    {
        // 13-digit numeric query routes to LookupByIsbnAsync (single
        // candidate or null) rather than the title/author search.
        _lookup.LookupByIsbnAsync("9780552131063", Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780552131063",
                Title: "Mort",
                Subtitle: null,
                Author: "Terry Pratchett",
                Publisher: "Corgi",
                GenreCandidates: [],
                DatePrinted: null,
                CoverUrl: "https://covers.example/mort.jpg",
                Source: "Open Library",
                Series: null,
                SeriesNumber: null,
                SeriesNumberRaw: null));

        var vm = CreateVm();
        vm.SearchQuery = "9780552131063";
        await vm.SearchAsync();

        var candidate = Assert.Single(vm.SearchCandidates);
        Assert.Equal("Mort", candidate.Title);
        Assert.Equal("Terry Pratchett", candidate.Author);
        Assert.Equal("https://covers.example/mort.jpg", candidate.CoverUrl);
        Assert.Equal("9780552131063", Assert.Single(candidate.Isbns));

        // Title/author search must NOT have been called for an ISBN-shaped query.
        await _lookup.DidNotReceive().SearchByTitleAuthorAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_TextQuery_CallsTitleAuthorSearchAndPopulatesCandidates()
    {
        // Non-ISBN-shaped query routes to SearchByTitleAuthorAsync —
        // up to 10 candidates. ISBNs come back empty (BookSearchCandidate
        // is work-level; ISBN-less wishlist entries are still valid).
        _lookup.SearchByTitleAuthorAsync("Foundation", null, Arg.Any<CancellationToken>())
            .Returns([
                new BookSearchCandidate(
                    WorkKey: "/works/OL1W",
                    Title: "Foundation",
                    Author: "Isaac Asimov",
                    FirstPublishYear: 1951,
                    EditionCount: 50,
                    CoverUrl: "https://covers.example/foundation.jpg",
                    OpenLibraryUrl: null),
                new BookSearchCandidate(
                    WorkKey: "/works/OL2W",
                    Title: "Foundation and Empire",
                    Author: "Isaac Asimov",
                    FirstPublishYear: 1952,
                    EditionCount: 30,
                    CoverUrl: null,
                    OpenLibraryUrl: null),
            ]);

        var vm = CreateVm();
        vm.SearchQuery = "Foundation";
        await vm.SearchAsync();

        Assert.Equal(2, vm.SearchCandidates.Count);
        Assert.All(vm.SearchCandidates, c => Assert.Empty(c.Isbns));
        Assert.Contains(vm.SearchCandidates, c => c.Title == "Foundation");
    }

    [Fact]
    public async Task SearchAsync_LookupThrows_SetsErrorMessageAndLeavesCandidatesEmpty()
    {
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BookLookupResult?>(_ => throw new HttpRequestException("upstream down"));

        var vm = CreateVm();
        vm.SearchQuery = "9780552131063";
        await vm.SearchAsync();

        Assert.NotNull(vm.SearchError);
        Assert.Empty(vm.SearchCandidates);
        Assert.True(vm.SearchedOnce);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_NoOps()
    {
        // Defensive — the page's Search button is disabled while empty,
        // but the VM is the single source of truth.
        var vm = CreateVm();
        vm.SearchQuery = "   ";
        await vm.SearchAsync();

        Assert.False(vm.SearchedOnce);
        Assert.Empty(vm.SearchCandidates);
        await _lookup.DidNotReceive().LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _lookup.DidNotReceive().SearchByTitleAuthorAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddCandidateAsync_PersistsCoverUrlAndIsbnsInBothColumns()
    {
        // Candidate from an ISBN lookup populates the legacy single
        // Isbn column (for back-compat row display) AND the new
        // WishlistItemIsbn table (for PR D's scan-flag lookup).
        var vm = CreateVm();
        var candidate = new WishlistViewModel.WishlistCandidate(
            Title: "The Hobbit",
            Author: "Tolkien",
            Isbns: ["9780261103252"],
            CoverUrl: "https://covers.example/hobbit.jpg",
            Source: "Open Library");

        var row = await vm.AddCandidateAsync(candidate);

        Assert.NotNull(row);
        Assert.Equal("The Hobbit", row!.Title);
        Assert.Equal("https://covers.example/hobbit.jpg", row.CoverUrl);
        Assert.Equal("9780261103252", row.Isbn);

        using var verify = _factory.CreateDbContext();
        var saved = await verify.WishlistItems
            .Include(w => w.Isbns)
            .SingleAsync();
        Assert.Equal("Tolkien", saved.Author);
        Assert.Equal("https://covers.example/hobbit.jpg", saved.CoverUrl);
        Assert.Equal("9780261103252", saved.Isbn); // legacy column
        var isbnRow = Assert.Single(saved.Isbns);   // new table
        Assert.Equal("9780261103252", isbnRow.Isbn);
    }

    [Fact]
    public async Task AddCandidateAsync_NoIsbns_PersistsWithoutIsbnRows()
    {
        // Title/author search candidates have no ISBNs. Wishlist row
        // saves anyway; PR D's scan-flag simply won't fire for them.
        var vm = CreateVm();
        var candidate = new WishlistViewModel.WishlistCandidate(
            Title: "Some Pre-ISBN Book",
            Author: "Author",
            Isbns: [],
            CoverUrl: null,
            Source: "Open Library");

        var row = await vm.AddCandidateAsync(candidate);

        Assert.NotNull(row);
        Assert.Null(row!.Isbn);

        using var verify = _factory.CreateDbContext();
        var saved = await verify.WishlistItems.Include(w => w.Isbns).SingleAsync();
        Assert.Null(saved.Isbn);
        Assert.Empty(saved.Isbns);
    }

    [Fact]
    public async Task AddCandidateAsync_NoTitle_ReturnsNullWithoutSaving()
    {
        var vm = CreateVm();
        var candidate = new WishlistViewModel.WishlistCandidate(
            Title: null,
            Author: "Author",
            Isbns: ["9780000000001"],
            CoverUrl: null,
            Source: "Open Library");

        var row = await vm.AddCandidateAsync(candidate);

        Assert.Null(row);
        using var verify = _factory.CreateDbContext();
        Assert.Empty(verify.WishlistItems);
    }

    [Fact]
    public async Task AddManualAsync_WithIsbn_AlsoWritesToWishlistItemIsbns()
    {
        // QuickAdd populates the new ISBN table too so PR D's scan-flag
        // catches manually-entered wishlist rows the same way it catches
        // search-and-add ones.
        var vm = CreateVm();
        vm.QuickAdd.Title = "Manual Title";
        vm.QuickAdd.Author = "Manual Author";
        vm.QuickAdd.Isbn = "9781234567897";

        await vm.AddManualAsync();

        using var verify = _factory.CreateDbContext();
        var saved = await verify.WishlistItems.Include(w => w.Isbns).SingleAsync();
        Assert.Equal("9781234567897", saved.Isbn);
        Assert.Equal("9781234567897", Assert.Single(saved.Isbns).Isbn);
    }

    [Fact]
    public async Task AddManualAsync_NoIsbn_LeavesNewTableEmpty()
    {
        var vm = CreateVm();
        vm.QuickAdd.Title = "Manual Title";
        vm.QuickAdd.Author = "Manual Author";
        vm.QuickAdd.Isbn = null;

        await vm.AddManualAsync();

        using var verify = _factory.CreateDbContext();
        var saved = await verify.WishlistItems.Include(w => w.Isbns).SingleAsync();
        Assert.Null(saved.Isbn);
        Assert.Empty(saved.Isbns);
    }

    [Fact]
    public async Task RemoveFromWishlistAsync_CascadesIsbns()
    {
        // FK cascade from WishlistItem → WishlistItemIsbn means removing
        // the parent cleans up the child ISBN rows. Locks the migration's
        // OnDelete behaviour.
        var vm = CreateVm();
        await vm.AddCandidateAsync(new WishlistViewModel.WishlistCandidate(
            Title: "X",
            Author: "Y",
            Isbns: ["9780000000001", "9780000000002"],
            CoverUrl: null,
            Source: "test"));

        int itemId;
        using (var db = _factory.CreateDbContext())
        {
            itemId = db.WishlistItems.Single().Id;
            Assert.Equal(2, db.WishlistItemIsbns.Count());
        }

        await vm.RemoveFromWishlistAsync(itemId);

        using var verify = _factory.CreateDbContext();
        Assert.Empty(verify.WishlistItems);
        Assert.Empty(verify.WishlistItemIsbns);
    }
}
