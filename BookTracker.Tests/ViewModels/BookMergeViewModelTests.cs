using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

public class BookMergeViewModelTests
{
    private readonly IBookMergeService _merger = Substitute.For<IBookMergeService>();

    private BookMergeViewModel CreateVm() => new(_merger);

    private static BookMergeDetail Detail(
        int id, string title,
        int rating = 0, string? notes = null, string? cover = null,
        IReadOnlyList<string>? works = null, IReadOnlyList<string>? tags = null) =>
        new(id, title, "A", BookCategory.Fiction, BookStatus.Read, rating, notes, DateTime.UtcNow,
            0, 0, works ?? [], tags ?? [], cover);

    [Fact]
    public async Task LoadAsync_populates_details()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(Detail(1, "A"), Detail(2, "B")));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.False(vm.Loading);
        Assert.Equal("A", vm.Lower!.Title);
        Assert.Equal("B", vm.Higher!.Title);
    }

    [Fact]
    public async Task EnrichmentHints_lists_every_gap_winner_has_that_loser_fills()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(
                Detail(1, "W", rating: 0, notes: null, cover: null),
                Detail(2, "L", rating: 4, notes: "Good one", cover: "https://x")));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var hints = vm.EnrichmentHints;

        Assert.Equal(3, hints.Count);
    }

    [Fact]
    public async Task WorksToUnion_counts_loser_works_winner_does_not_have()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(
                Detail(1, "W", works: ["Shared", "WinnerOnly"]),
                Detail(2, "L", works: ["Shared", "LoserOnly1", "LoserOnly2"])));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        Assert.Equal(2, vm.WorksToUnion);
    }

    [Fact]
    public async Task TagsToUnion_counts_loser_tags_winner_does_not_have()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(
                Detail(1, "W", tags: ["alpha"]),
                Detail(2, "L", tags: ["alpha", "beta", "gamma"])));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        Assert.Equal(2, vm.TagsToUnion);
    }

    [Fact]
    public async Task MergeAsync_delegates_to_service()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(Detail(1, "W"), Detail(2, "L")));
        _merger.MergeAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new BookMergeResult(true, null, 1, 2, 0, 1, "W", "L"));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var result = await vm.MergeAsync();

        Assert.NotNull(result);
        Assert.True(result!.Success);
        await _merger.Received(1).MergeAsync(1, 2, Arg.Any<CancellationToken>());
    }
}
