using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Unit)]
public class EditionMergeViewModelTests
{
    private readonly IEditionMergeService _merger = Substitute.For<IEditionMergeService>();

    private EditionMergeViewModel CreateVm() => new(_merger);

    private static EditionMergeDetail Detail(int id, string? isbn = null, string? coverUrl = null, string? publisherName = null, DateOnly? datePrinted = null) =>
        new(id, isbn, BookFormat.Hardcover, publisherName, datePrinted, DatePrecision.Day, 0, 1, "Book", coverUrl);

    [Fact]
    public async Task LoadAsync_populates_details()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new EditionMergeLoadResult(Detail(1, isbn: "A"), Detail(2, isbn: "B"), null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.False(vm.Loading);
        Assert.Null(vm.IncompatibilityReason);
    }

    [Fact]
    public async Task LoadAsync_surfaces_incompatibility_reason()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new EditionMergeLoadResult(Detail(1), Detail(2), "cross-book"));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.Equal("cross-book", vm.IncompatibilityReason);
        Assert.False(vm.CanMerge);
    }

    [Fact]
    public async Task EnrichmentHints_lists_fields_winner_will_take_from_loser()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new EditionMergeLoadResult(
                Detail(1, isbn: null, coverUrl: null, publisherName: null, datePrinted: null),
                Detail(2, isbn: "978123", coverUrl: "https://x", publisherName: "P", datePrinted: new DateOnly(2020, 5, 1)),
                null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var hints = vm.EnrichmentHints;

        Assert.Equal(4, hints.Count);
    }

    [Fact]
    public async Task EnrichmentHints_returns_empty_when_winner_has_everything()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new EditionMergeLoadResult(
                Detail(1, isbn: "978", coverUrl: "c", publisherName: "P", datePrinted: new DateOnly(2020, 1, 1)),
                Detail(2, isbn: "999", coverUrl: "d", publisherName: "Q", datePrinted: new DateOnly(1999, 1, 1)),
                null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        Assert.Empty(vm.EnrichmentHints);
    }

    [Fact]
    public async Task MergeAsync_delegates_to_service()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new EditionMergeLoadResult(Detail(1), Detail(2), null));
        _merger.MergeAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new EditionMergeResult(true, null, 2, 1, "A", "B"));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var result = await vm.MergeAsync();

        Assert.NotNull(result);
        Assert.True(result!.Success);
        await _merger.Received(1).MergeAsync(1, 2, Arg.Any<CancellationToken>());
    }
}
