using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Unit)]
public class BookMergeViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private BookMergeViewModel CreateVm() => new(_dispatcher);

    private static BookMergeDetail Detail(
        int id, string title,
        int rating = 0, string? notes = null, string? cover = null,
        IReadOnlyList<string>? works = null, IReadOnlyList<string>? tags = null) =>
        new(id, title, "A", BookCategory.Fiction, BookStatus.Read, rating, notes, DateTime.UtcNow,
            0, 0, works ?? [], tags ?? [], cover);

    [Fact]
    public async Task LoadAsync_populates_details()
    {
        _dispatcher.Query(Arg.Any<GetBookMergePreview>(), Arg.Any<CancellationToken>())
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
        _dispatcher.Query(Arg.Any<GetBookMergePreview>(), Arg.Any<CancellationToken>())
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
        _dispatcher.Query(Arg.Any<GetBookMergePreview>(), Arg.Any<CancellationToken>())
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
        _dispatcher.Query(Arg.Any<GetBookMergePreview>(), Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(
                Detail(1, "W", tags: ["alpha"]),
                Detail(2, "L", tags: ["alpha", "beta", "gamma"])));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        Assert.Equal(2, vm.TagsToUnion);
    }

    [Fact]
    public async Task MergeAsync_dispatches_merge_command()
    {
        _dispatcher.Query(Arg.Any<GetBookMergePreview>(), Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(Detail(1, "W"), Detail(2, "L")));
        _dispatcher.Send(Arg.Any<MergeBooks>(), Arg.Any<CancellationToken>())
            .Returns(new BookMergeResult(true, null, 1, 2, 0, 1, "W", "L"));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var result = await vm.MergeAsync();

        Assert.NotNull(result);
        Assert.True(result!.Success);
        await _dispatcher.Received(1).Send(
            Arg.Is<MergeBooks>(c => c!.WinnerId == 1 && c.LoserId == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeAsync_domain_refusal_sets_Error_and_returns_null()
    {
        _dispatcher.Query(Arg.Any<GetBookMergePreview>(), Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(Detail(1, "W"), Detail(2, "L")));
        _dispatcher.Send(Arg.Any<MergeBooks>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("A copy is lent out."));
        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var result = await vm.MergeAsync();

        Assert.Null(result);
        Assert.Equal("A copy is lent out.", vm.Error);
        Assert.False(vm.Merging);
    }

    [Fact]
    public async Task MergeAsync_fault_propagates_and_leaves_Error_clear()
    {
        _dispatcher.Query(Arg.Any<GetBookMergePreview>(), Arg.Any<CancellationToken>())
            .Returns(new BookMergeLoadResult(Detail(1, "W"), Detail(2, "L")));
        _dispatcher.Send(Arg.Any<MergeBooks>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));
        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.MergeAsync());

        Assert.Null(vm.Error);
        Assert.False(vm.Merging);
    }
}
