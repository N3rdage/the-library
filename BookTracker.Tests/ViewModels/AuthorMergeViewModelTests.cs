using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

public class AuthorMergeViewModelTests
{
    private readonly IAuthorMergeService _merger = Substitute.For<IAuthorMergeService>();

    private AuthorMergeViewModel CreateVm() => new(_merger);

    private static AuthorMergeDetail Detail(int id, string name) =>
        new(id, name, null, null, 0, 0, [], null);

    [Fact]
    public async Task LoadAsync_populates_both_details_and_clears_loading()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.False(vm.Loading);
        Assert.Equal("A", vm.Lower!.Name);
        Assert.Equal("B", vm.Higher!.Name);
        Assert.Null(vm.IncompatibilityReason);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_sets_error_when_entity_missing()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(null, Detail(2, "B"), null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_surfaces_incompatibility_reason()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(Detail(1, "A"), Detail(2, "B"), "nope"));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.Equal("nope", vm.IncompatibilityReason);
        Assert.False(vm.CanMerge);
    }

    [Fact]
    public async Task CanMerge_requires_a_selected_winner()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        Assert.False(vm.CanMerge);
        vm.SelectedWinnerId = 1;
        Assert.True(vm.CanMerge);
    }

    [Fact]
    public async Task LoserId_flips_based_on_selected_winner()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        vm.SelectedWinnerId = 1;
        Assert.Equal(2, vm.LoserId);
        vm.SelectedWinnerId = 2;
        Assert.Equal(1, vm.LoserId);
    }

    [Fact]
    public async Task MergeAsync_calls_service_with_picked_winner_and_loser()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null));
        _merger.MergeAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeResult(true, null, 3, 1, false, "A", "B"));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        var result = await vm.MergeAsync();

        Assert.NotNull(result);
        Assert.True(result!.Success);
        await _merger.Received(1).MergeAsync(1, 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MergeAsync_surfaces_error_when_service_fails()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null));
        _merger.MergeAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeResult(false, "boom", 0, 0, false, null, null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);
        vm.SelectedWinnerId = 1;

        await vm.MergeAsync();

        Assert.Equal("boom", vm.ErrorMessage);
    }

    [Fact]
    public async Task MergeAsync_is_noop_when_no_winner_selected()
    {
        _merger.LoadAsync(1, 2, Arg.Any<CancellationToken>())
            .Returns(new AuthorMergeLoadResult(Detail(1, "A"), Detail(2, "B"), null));

        var vm = CreateVm();
        await vm.LoadAsync(1, 2);

        var result = await vm.MergeAsync();

        Assert.Null(result);
        await _merger.DidNotReceiveWithAnyArgs().MergeAsync(default, default, default);
    }
}
