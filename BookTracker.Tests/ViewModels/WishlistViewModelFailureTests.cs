using BookTracker.Application;
using BookTracker.Application.Wishlist;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// The failure path of the wishlist commands against a substituted dispatcher: a
// domain refusal lands in Error and leaves the list as it was; a fault
// propagates. What the commands persist is covered by WishlistViewModelTests
// and the handler tests.
[Trait("Category", TestCategories.Unit)]
public class WishlistViewModelFailureTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private WishlistViewModel CreateVm() =>
        new(Substitute.For<IBookLookupService>(), _dispatcher, NullLogger<WishlistViewModel>.Instance);

    private static WishlistViewModel.WishlistCandidate Candidate() =>
        new("Mort", "Terry Pratchett", [], null, "Open Library");

    [Fact]
    public async Task AddCandidateAsync_DomainRefusal_SetsErrorAndReturnsNull()
    {
        _dispatcher.Send(Arg.Is<AddWishlistItem>(c => c != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("Priority is invalid."));
        var vm = CreateVm();

        var row = await vm.AddCandidateAsync(Candidate());

        Assert.Null(row);
        Assert.Equal("Priority is invalid.", vm.Error);
        Assert.Empty(vm.Wishlist);
    }

    [Fact]
    public async Task AddManualAsync_DomainRefusal_SetsErrorAndKeepsTheQuickAddInput()
    {
        _dispatcher.Send(Arg.Is<AddWishlistItem>(c => c != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("ISBN is invalid."));
        var vm = CreateVm();
        vm.ShowingQuickAdd = true;
        vm.QuickAdd.Title = "Mort";

        await vm.AddManualAsync();

        Assert.Equal("ISBN is invalid.", vm.Error);
        Assert.True(vm.ShowingQuickAdd);
        Assert.Equal("Mort", vm.QuickAdd.Title);
    }

    [Fact]
    public async Task AddSeriesSlotsToWishlistAsync_NotFound_SetsErrorAndReturnsNull()
    {
        _dispatcher.Send(Arg.Is<AddWishlistSeriesSlots>(c => c != null), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("Series 3 not found."));
        var vm = CreateVm();

        var added = await vm.AddSeriesSlotsToWishlistAsync(3, [1, 2]);

        Assert.Null(added);
        Assert.Equal("Series 3 not found.", vm.Error);
    }

    [Fact]
    public async Task RemoveFromWishlistAsync_NotFound_SetsErrorAndKeepsTheRow()
    {
        _dispatcher.Query(Arg.Any<GetWishlist>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WishlistRow(7, "Mort", "Terry Pratchett", WishlistPriority.Medium, null, null, null, null) });
        var vm = CreateVm();
        await vm.LoadWishlistAsync();
        _dispatcher.Send(Arg.Any<RemoveWishlistItem>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("Wishlist item 7 not found."));

        await vm.RemoveFromWishlistAsync(7);

        Assert.Equal("Wishlist item 7 not found.", vm.Error);
        Assert.Single(vm.Wishlist);
    }

    [Fact]
    public async Task MarkAsBoughtAsync_DomainRefusal_SetsErrorKeepsTheRowAndReturnsNull()
    {
        var item = new WishlistRow(7, "Mort", "Terry Pratchett", WishlistPriority.Medium, null, null, null, null);
        _dispatcher.Query(Arg.Any<GetWishlist>(), Arg.Any<CancellationToken>()).Returns(new[] { item });
        var vm = CreateVm();
        await vm.LoadWishlistAsync();
        _dispatcher.Send(Arg.Any<MarkWishlistItemBought>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("Cannot mark this item bought."));

        var bookId = await vm.MarkAsBoughtAsync(item);

        Assert.Null(bookId);
        Assert.Equal("Cannot mark this item bought.", vm.Error);
        Assert.Single(vm.Wishlist);
    }

    [Fact]
    public async Task MarkAsBoughtAsync_Fault_Propagates()
    {
        var item = new WishlistRow(7, "Mort", "Terry Pratchett", WishlistPriority.Medium, null, null, null, null);
        _dispatcher.Send(Arg.Any<MarkWishlistItemBought>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("db down"));
        var vm = CreateVm();

        await Assert.ThrowsAsync<TimeoutException>(() => vm.MarkAsBoughtAsync(item));

        Assert.Null(vm.Error);
    }
}
