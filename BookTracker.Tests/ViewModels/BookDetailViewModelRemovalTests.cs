using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Wishlist;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// Removed copies on the detail VM: what counts as lendable, restore, and the
// wishlist offer after a lost or damaged copy. A substituted dispatcher
// stands in; the SQL is covered by the handler tests.
[Trait("Category", TestCategories.Unit)]
public class BookDetailViewModelRemovalTests
{
    private const int BookId = 7;
    private static readonly DateOnly Removed = new(2026, 11, 3);

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static CopyDetail Owned(int id) => new(id, BookCondition.Good, null, null);

    private static CopyDetail Gone(int id, CopyDisposition reason = CopyDisposition.Donated) =>
        new(id, BookCondition.Good, null, null, null, reason, Removed, "note");

    private async Task<BookDetailViewModel> LoadedWithAsync(string? isbn, string? bookCover, string? editionCover, params CopyDetail[] copies)
    {
        var work = new WorkDetail(1, "Dune", null, "Frank Herbert", 5, "", []);
        var detail = new BookDetail(
            BookId, "Dune", BookCategory.Fiction, BookStatus.Unread, 0, null, bookCover, new DateTime(2026, 1, 1),
            Works: [work],
            Editions: [new EditionDetail(3, isbn, BookFormat.Hardcover, "Hardcover", null, "", editionCover, false, copies)],
            Tags: [],
            Series: null);
        _dispatcher.Query(Arg.Any<GetBookDetail>(), Arg.Any<CancellationToken>()).Returns(detail);
        _dispatcher.Query(Arg.Any<GetWishlistDuplicate>(), Arg.Any<CancellationToken>()).Returns(new WishlistDuplicate(null, null));
        var vm = new BookDetailViewModel(
            new CoverPhotoUploader(Substitute.For<IBookCoverStorage>(), NullLogger<CoverPhotoUploader>.Instance),
            _dispatcher, TimeProvider.System);
        await vm.InitializeAsync(BookId);
        return vm;
    }

    private Task<BookDetailViewModel> LoadedVmAsync(params CopyDetail[] copies) =>
        LoadedWithAsync("9780441013593", "https://covers/book.jpg", null, copies);

    [Fact]
    public async Task RemovedCopy_isNotLendable_andIsCountedApart()
    {
        var vm = await LoadedVmAsync(Gone(10), Owned(11));

        Assert.Equal(11, vm.FirstFreeCopyId);
        Assert.Equal(1, vm.TotalCopies);
        Assert.Equal(1, vm.RemovedCopies);
        Assert.Null(vm.LendOptionFor(10));
        Assert.NotNull(vm.LendOptionFor(11));
    }

    [Fact]
    public async Task OnlyRemovedCopies_hasNothingToLend_andIsNotAllLent()
    {
        var vm = await LoadedVmAsync(Gone(10));

        Assert.Null(vm.FirstFreeCopyId);
        Assert.False(vm.AllCopiesLent);
        Assert.Null(vm.LentToLabel);
    }

    [Fact]
    public async Task LentToLabel_ignoresRemovedCopies()
    {
        var out1 = new CopyDetail(11, BookCondition.Good, null, null, new CopyLoanDetail(1, "Ann", Removed, null));
        var vm = await LoadedVmAsync(Gone(10), out1);

        Assert.Equal("Lent to Ann", vm.LentToLabel);
    }

    [Fact]
    public async Task CopyLabelFor_namesTheBookAndEdition()
    {
        var vm = await LoadedVmAsync(Owned(11));

        Assert.Equal("Dune · Hardcover · 9780441013593", vm.CopyLabelFor(11));
        Assert.Null(vm.CopyLabelFor(999));
    }

    [Fact]
    public async Task RestoreCopyAsync_sendsRestore_andReloads()
    {
        var vm = await LoadedVmAsync(Gone(10));
        _dispatcher.ClearReceivedCalls();

        Assert.True(await vm.RestoreCopyAsync(10));

        await _dispatcher.Received(1).Send(Arg.Is<RestoreCopyToCollection>(c => c!.CopyId == 10), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Query(Arg.Is<GetBookDetail>(q => q!.BookId == BookId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreCopyAsync_refused_setsError_andDoesNotReload()
    {
        var vm = await LoadedVmAsync(Owned(10));
        _dispatcher.ClearReceivedCalls();
        _dispatcher.Send(Arg.Any<RestoreCopyToCollection>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("This copy is already in the collection."));

        Assert.False(await vm.RestoreCopyAsync(10));

        Assert.Equal("This copy is already in the collection.", vm.Error);
        await _dispatcher.DidNotReceive().Query(Arg.Any<GetBookDetail>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CopyDisposition.Lost, true)]
    [InlineData(CopyDisposition.Damaged, true)]
    [InlineData(CopyDisposition.Donated, false)]
    [InlineData(CopyDisposition.Sold, false)]
    public void ShouldOfferWishlist_onlyForLostAndDamaged(CopyDisposition reason, bool expected) =>
        Assert.Equal(expected, BookDetailViewModel.ShouldOfferWishlist(reason));

    [Fact]
    public async Task AddToWishlistAsync_sendsTheBookWithMediumPriority_isbnAndCover()
    {
        var vm = await LoadedVmAsync(Gone(10, CopyDisposition.Lost));

        var outcome = await vm.AddToWishlistAsync(10);

        Assert.Equal(WishlistOfferOutcome.Added, outcome);
        await _dispatcher.Received(1).Send(
            Arg.Is<AddWishlistItem>(c =>
                c!.Title == "Dune" && c.Author == "Frank Herbert" && c.Priority == WishlistPriority.Medium
                && c.Isbns.SequenceEqual(new[] { "9780441013593" }) && c.CoverUrl == "https://covers/book.jpg"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddToWishlistAsync_noIsbn_fallsBackToTheEditionCover()
    {
        var vm = await LoadedWithAsync(null, null, "https://covers/edition.jpg", Gone(10, CopyDisposition.Damaged));

        await vm.AddToWishlistAsync(10);

        await _dispatcher.Received(1).Send(
            Arg.Is<AddWishlistItem>(c => c!.Isbns.Count == 0 && c.CoverUrl == "https://covers/edition.jpg"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddToWishlistAsync_isbnAlreadyWishlisted_skips()
    {
        var vm = await LoadedVmAsync(Gone(10, CopyDisposition.Lost));
        _dispatcher.Query(Arg.Any<GetWishlistDuplicate>(), Arg.Any<CancellationToken>()).Returns(new WishlistDuplicate(null, 5));

        var outcome = await vm.AddToWishlistAsync(10);

        Assert.Equal(WishlistOfferOutcome.AlreadyOnWishlist, outcome);
        await _dispatcher.DidNotReceive().Send(Arg.Any<AddWishlistItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddToWishlistAsync_titleAndAuthorAlreadyWishlisted_skips()
    {
        var vm = await LoadedWithAsync(null, null, null, Gone(10, CopyDisposition.Lost));
        _dispatcher.Query(Arg.Is<GetWishlistItemExists>(q => q!.Title == "Dune" && q.Author == "Frank Herbert"), Arg.Any<CancellationToken>())
            .Returns(true);

        var outcome = await vm.AddToWishlistAsync(10);

        Assert.Equal(WishlistOfferOutcome.AlreadyOnWishlist, outcome);
        await _dispatcher.DidNotReceive().Send(Arg.Any<AddWishlistItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddToWishlistAsync_unknownCopy_addsNothing()
    {
        var vm = await LoadedVmAsync(Owned(11));

        Assert.Equal(WishlistOfferOutcome.NotAdded, await vm.AddToWishlistAsync(999));
        await _dispatcher.DidNotReceive().Send(Arg.Any<AddWishlistItem>(), Arg.Any<CancellationToken>());
    }
}
