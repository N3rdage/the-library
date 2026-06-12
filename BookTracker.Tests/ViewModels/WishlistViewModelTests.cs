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
    public async Task SearchAsync_IsbnAlreadyOwned_FlagsCandidateWithBookIdAndDoesNotBlockAdd()
    {
        // Drew's case 2026-05-25: ISBN search for a book he already owns
        // should warn before he adds it to the wishlist (still allows the
        // add — backup-copy intent is legitimate — just surfaces the
        // duplicate so it's a conscious choice).
        int bookId;
        using (var db = _factory.CreateDbContext())
        {
            var author = new Author { Name = "Tolkien" };
            var book = new Book
            {
                Title = "The Hobbit",
                Works = [new Work { Title = "The Hobbit", WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }] }],
                Editions = [new Edition { Isbn = "9780261103252", Format = BookFormat.Hardcover }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        _lookup.LookupByIsbnAsync("9780261103252", Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780261103252",
                Title: "The Hobbit",
                Subtitle: null, Author: "Tolkien", Publisher: null,
                GenreCandidates: [], DatePrinted: null,
                CoverUrl: null, Source: "Open Library",
                Series: null, SeriesNumber: null, SeriesNumberRaw: null));

        var vm = CreateVm();
        vm.SearchQuery = "9780261103252";
        await vm.SearchAsync();

        var candidate = Assert.Single(vm.SearchCandidates);
        Assert.Equal(bookId, candidate.AlreadyOwnedBookId);
        Assert.Null(candidate.AlreadyWishlistedItemId);
    }

    [Fact]
    public async Task SearchAsync_IsbnAlreadyOnWishlist_FlagsCandidateWithWishlistItemId()
    {
        // Same shape but the ISBN matches an existing wishlist row
        // (either the legacy single column or the new ISBN table — this
        // test seeds via the new table to cover the union branch).
        int wishlistId;
        using (var db = _factory.CreateDbContext())
        {
            var item = new WishlistItem
            {
                Title = "Foundation",
                Author = "Asimov",
                Isbns = [new WishlistItemIsbn { Isbn = "9780553293357" }],
            };
            db.WishlistItems.Add(item);
            await db.SaveChangesAsync();
            wishlistId = item.Id;
        }

        _lookup.LookupByIsbnAsync("9780553293357", Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780553293357",
                Title: "Foundation",
                Subtitle: null, Author: "Asimov", Publisher: null,
                GenreCandidates: [], DatePrinted: null,
                CoverUrl: null, Source: "Open Library",
                Series: null, SeriesNumber: null, SeriesNumberRaw: null));

        var vm = CreateVm();
        vm.SearchQuery = "9780553293357";
        await vm.SearchAsync();

        var candidate = Assert.Single(vm.SearchCandidates);
        Assert.Null(candidate.AlreadyOwnedBookId);
        Assert.Equal(wishlistId, candidate.AlreadyWishlistedItemId);
    }

    [Fact]
    public async Task SearchAsync_IsbnNeitherOwnedNorWishlisted_LeavesBothFlagsNull()
    {
        _lookup.LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9789999999999",
                Title: "Unowned",
                Subtitle: null, Author: "Nobody", Publisher: null,
                GenreCandidates: [], DatePrinted: null,
                CoverUrl: null, Source: "Open Library",
                Series: null, SeriesNumber: null, SeriesNumberRaw: null));

        var vm = CreateVm();
        vm.SearchQuery = "9789999999999";
        await vm.SearchAsync();

        var candidate = Assert.Single(vm.SearchCandidates);
        Assert.Null(candidate.AlreadyOwnedBookId);
        Assert.Null(candidate.AlreadyWishlistedItemId);
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
    public async Task SearchAdvancedAsync_TitleAndAuthor_CallsSearchByTitleAuthorWithBothFields()
    {
        // The motivating case: "Martin Grant" the author surfaced books
        // with "Martin" / "Grant" in the title under the simple query
        // because the wrapper put everything in the title field. With
        // the advanced expander the author field is scoped on its own.
        _lookup.SearchByTitleAuthorAsync("mutant", "Martin Grant", Arg.Any<CancellationToken>())
            .Returns([
                new BookSearchCandidate(
                    WorkKey: "/works/OL1W",
                    Title: "Mutant Trooper",
                    Author: "Martin Grant",
                    FirstPublishYear: null, EditionCount: null,
                    CoverUrl: null, OpenLibraryUrl: null),
            ]);

        var vm = CreateVm();
        vm.AdvancedSearchOpen = true;
        vm.AdvancedTitle = "mutant";
        vm.AdvancedAuthor = "Martin Grant";
        await vm.SearchAdvancedAsync();

        await _lookup.Received(1).SearchByTitleAuthorAsync(
            "mutant", "Martin Grant", Arg.Any<CancellationToken>());
        Assert.Single(vm.SearchCandidates);
    }

    [Fact]
    public async Task SearchAdvancedAsync_AuthorOnly_PassesNullForTitle()
    {
        _lookup.SearchByTitleAuthorAsync(null, "Martin Grant", Arg.Any<CancellationToken>())
            .Returns([
                new BookSearchCandidate(
                    WorkKey: "/works/OL1W",
                    Title: "Mutant Trooper",
                    Author: "Martin Grant",
                    FirstPublishYear: null, EditionCount: null,
                    CoverUrl: null, OpenLibraryUrl: null),
            ]);

        var vm = CreateVm();
        vm.AdvancedSearchOpen = true;
        vm.AdvancedAuthor = "Martin Grant";
        await vm.SearchAdvancedAsync();

        await _lookup.Received(1).SearchByTitleAuthorAsync(
            null, "Martin Grant", Arg.Any<CancellationToken>());
        Assert.Single(vm.SearchCandidates);
    }

    [Fact]
    public async Task SearchAdvancedAsync_IsbnFieldFilled_WinsOverTitleAndAuthor()
    {
        // The ISBN field is the more specific identifier; if filled it
        // takes priority and routes to LookupByIsbnAsync with the same
        // duplicate-detection treatment as the simple-box ISBN path.
        _lookup.LookupByIsbnAsync("9780261103252", Arg.Any<CancellationToken>())
            .Returns(new BookLookupResult(
                Isbn: "9780261103252",
                Title: "The Hobbit",
                Subtitle: null, Author: "Tolkien", Publisher: null,
                GenreCandidates: [], DatePrinted: null,
                CoverUrl: null, Source: "Open Library",
                Series: null, SeriesNumber: null, SeriesNumberRaw: null));

        var vm = CreateVm();
        vm.AdvancedSearchOpen = true;
        vm.AdvancedTitle = "Something Else";
        vm.AdvancedAuthor = "Someone Else";
        vm.AdvancedIsbn = "9780261103252";
        await vm.SearchAdvancedAsync();

        await _lookup.Received(1).LookupByIsbnAsync("9780261103252", Arg.Any<CancellationToken>());
        await _lookup.DidNotReceive().SearchByTitleAuthorAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        var candidate = Assert.Single(vm.SearchCandidates);
        Assert.Equal("The Hobbit", candidate.Title);
    }

    [Fact]
    public async Task SearchAdvancedAsync_AllFieldsEmpty_NoOps()
    {
        var vm = CreateVm();
        vm.AdvancedSearchOpen = true;
        await vm.SearchAdvancedAsync();

        Assert.False(vm.SearchedOnce);
        Assert.Empty(vm.SearchCandidates);
        await _lookup.DidNotReceive().LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _lookup.DidNotReceive().SearchByTitleAuthorAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAdvancedAsync_InvalidIsbn_SetsErrorMessage()
    {
        // ISBN field with non-ISBN content (e.g. user typed a title there
        // by mistake) surfaces a validation error instead of routing to
        // either lookup path.
        var vm = CreateVm();
        vm.AdvancedSearchOpen = true;
        vm.AdvancedIsbn = "not an isbn";
        await vm.SearchAdvancedAsync();

        Assert.NotNull(vm.SearchError);
        Assert.Empty(vm.SearchCandidates);
        await _lookup.DidNotReceive().LookupByIsbnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearSearch_AlsoClearsAdvancedFields()
    {
        var vm = CreateVm();
        vm.AdvancedTitle = "x";
        vm.AdvancedAuthor = "y";
        vm.AdvancedIsbn = "9780000000001";
        vm.SearchQuery = "simple";

        vm.ClearSearch();

        Assert.Equal("", vm.SearchQuery);
        Assert.Equal("", vm.AdvancedTitle);
        Assert.Equal("", vm.AdvancedAuthor);
        Assert.Equal("", vm.AdvancedIsbn);
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
    public async Task AddSeriesSlotsToWishlistAsync_CreatesStubsWithSeriesBadgeAndOrder()
    {
        // Finite-series Add-selected flow: each picked slot becomes one
        // wishlist row stubbed as "{SeriesName} #{slot}" with the
        // series's display author. SeriesId + SeriesOrder set so the
        // row renders with the series badge and lines up against gap
        // detection.
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Foundation", Author = "Isaac Asimov", Type = SeriesType.Series, ExpectedCount = 7 };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var vm = CreateVm();
        var added = await vm.AddSeriesSlotsToWishlistAsync(seriesId, [4, 6, 7]);

        Assert.Equal(3, added);
        using var verify = _factory.CreateDbContext();
        var rows = await verify.WishlistItems.OrderBy(w => w.SeriesOrder).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Foundation #4", rows[0].Title);
        Assert.Equal("Isaac Asimov", rows[0].Author);
        Assert.Equal(seriesId, rows[0].SeriesId);
        Assert.Equal(4, rows[0].SeriesOrder);
        Assert.Equal("Foundation #7", rows[2].Title);
    }

    [Fact]
    public async Task AddSeriesSlotsToWishlistAsync_SkipsAlreadyWishlistedSlots()
    {
        // Idempotent re-runs: asking for slots 4–6 when slot 4 is
        // already wishlisted adds only 5 and 6. Lets the user re-tick
        // a set without spinning up duplicates.
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Foundation", Type = SeriesType.Series, ExpectedCount = 7 };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seriesId = series.Id;
            db.WishlistItems.Add(new WishlistItem
            {
                Title = "Foundation #4",
                Author = "Asimov",
                SeriesId = seriesId,
                SeriesOrder = 4,
            });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        var added = await vm.AddSeriesSlotsToWishlistAsync(seriesId, [4, 5, 6]);

        Assert.Equal(2, added);
        using var verify = _factory.CreateDbContext();
        Assert.Equal(3, verify.WishlistItems.Count()); // existing + 2 new
        Assert.DoesNotContain(
            verify.WishlistItems.Where(w => w.SeriesOrder == 4),
            w => w.Title == "Foundation #4 duplicate");
    }

    [Fact]
    public async Task AddSeriesSlotsToWishlistAsync_NullSeriesAuthor_FallsBackToUnknown()
    {
        // Series.Author is optional. Stubs need a non-empty Author per
        // the WishlistItem schema; fallback string keeps the row valid.
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var series = new Series { Name = "Mystery Series", Type = SeriesType.Series, ExpectedCount = 5, Author = null };
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var vm = CreateVm();
        await vm.AddSeriesSlotsToWishlistAsync(seriesId, [1]);

        using var verify = _factory.CreateDbContext();
        var row = await verify.WishlistItems.SingleAsync();
        Assert.Equal("Unknown", row.Author);
    }

    [Fact]
    public async Task AddSeriesSlotsToWishlistAsync_UnknownSeriesId_ReturnsZero()
    {
        var vm = CreateVm();
        var added = await vm.AddSeriesSlotsToWishlistAsync(9999, [1, 2]);
        Assert.Equal(0, added);
    }

    [Fact]
    public async Task LoadSeriesGapsAsync_FlooredInterquel_DoesNotMaskRealNumberedGap()
    {
        // An interquel ("4.5" -> SeriesOrder 4, SeriesOrderDisplay "4.5") must
        // NOT count as owning slot #4 — otherwise the genuinely-missing real
        // #4 is hidden from the gap card. Own #1,2,3,5 + the interquel; #4 is
        // absent and must still surface as missing.
        int seriesId;
        using (var db = _factory.CreateDbContext())
        {
            var author = new Author { Name = "Brandon Sanderson" };
            var series = new Series { Name = "The Stormlight Archive", Author = "Brandon Sanderson", Type = SeriesType.Series, ExpectedCount = 5 };
            foreach (var (order, display) in new (int?, string?)[] { (1, null), (2, null), (3, null), (5, null), (4, "4.5") })
            {
                var work = new Work
                {
                    Title = $"Work {display ?? order?.ToString()}",
                    Series = series,
                    SeriesOrder = order,
                    SeriesOrderDisplay = display,
                    WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
                };
                db.Books.Add(new Book { Title = $"Book {display ?? order?.ToString()}", Works = [work] });
            }
            db.Series.Add(series);
            await db.SaveChangesAsync();
            seriesId = series.Id;
        }

        var vm = CreateVm();
        await vm.LoadSeriesGapsAsync();

        var gap = vm.SeriesGaps.Single(g => g.SeriesId == seriesId);
        Assert.Contains(4, gap.MissingPositions);   // real #4 still flagged missing
        Assert.Equal(4, gap.OwnedCount);            // 1,2,3,5 counted; interquel not
    }

    [Fact]
    public async Task LoadSeriesGapsAsync_PopulatesOpenSeriesList_WithNullExpectedCountAndOwnedBooks()
    {
        // Open-ended series — no ExpectedCount, owns at least one book.
        // Surfaces in OpenSeriesList for the "Add next N" flow.
        using (var db = _factory.CreateDbContext())
        {
            var author = new Author { Name = "Terry Pratchett" };
            var discworld = new Series { Name = "Discworld", Author = "Terry Pratchett", Type = SeriesType.Series, ExpectedCount = null };
            db.Series.Add(discworld);
            await db.SaveChangesAsync();

            var book = new Book
            {
                Title = "Mort",
                Works = [new Work
                {
                    Title = "Mort",
                    SeriesId = discworld.Id,
                    SeriesOrder = 4,
                    WorkAuthors = [new WorkAuthor { Author = author, Order = 0 }],
                }],
            };
            db.Books.Add(book);
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        await vm.LoadSeriesGapsAsync();

        var open = Assert.Single(vm.OpenSeriesList);
        Assert.Equal("Discworld", open.SeriesName);
        Assert.Equal("Terry Pratchett", open.Author);
        Assert.Equal(1, open.OwnedCount);
        Assert.Equal(4, open.HighestOwnedOrder);
        Assert.Equal([4], open.OwnedOrders);
    }

    [Fact]
    public async Task LoadSeriesGapsAsync_OpenSeriesList_EmptyWhenSeriesHasNoOwnedBooks()
    {
        // A standalone Series row (no Works) shouldn't surface in
        // OpenSeriesList — the user has nothing to extrapolate from.
        using (var db = _factory.CreateDbContext())
        {
            db.Series.Add(new Series { Name = "Empty Series", Type = SeriesType.Series, ExpectedCount = null });
            await db.SaveChangesAsync();
        }

        var vm = CreateVm();
        await vm.LoadSeriesGapsAsync();

        Assert.Empty(vm.OpenSeriesList);
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
