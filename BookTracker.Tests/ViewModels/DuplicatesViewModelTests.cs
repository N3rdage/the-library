using BookTracker.Application;
using BookTracker.Application.Duplicates;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

[Trait("Category", TestCategories.Unit)]
public class DuplicatesViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private DuplicatesViewModel CreateVm() => new(_dispatcher);

    private static AuthorDuplicatePair ActiveAuthorPair(int a, int b) =>
        new(new DuplicateAuthorView(a, $"Author {a}", 0, null, null),
            new DuplicateAuthorView(b, $"Author {b}", 0, null, null),
            null, "test");

    private static AuthorDuplicatePair DismissedAuthorPair(int a, int b, int ignoredId) =>
        new(new DuplicateAuthorView(a, $"Author {a}", 0, null, null),
            new DuplicateAuthorView(b, $"Author {b}", 0, null, null),
            new DismissalInfo(ignoredId, DateTime.UtcNow, null),
            "test");

    private void ReturnsReport(DuplicateReport report) =>
        _dispatcher.Query(Arg.Any<GetDuplicateReport>(), Arg.Any<CancellationToken>()).Returns(report);

    [Fact]
    public async Task LoadAsync_populates_active_and_dismissed_splits_per_tab()
    {
        ReturnsReport(new DuplicateReport(
            Authors: [ActiveAuthorPair(1, 2), DismissedAuthorPair(3, 4, 99)],
            Works: [],
            Books: [],
            Editions: []));

        var vm = CreateVm();
        await vm.LoadAsync();

        Assert.False(vm.Loading);
        Assert.Single(vm.ActiveAuthorPairs);
        Assert.Single(vm.DismissedAuthorPairs);
        Assert.Equal(1, vm.ActiveCount(DuplicateEntityType.Author));
        Assert.Equal(0, vm.ActiveCount(DuplicateEntityType.Work));
    }

    [Fact]
    public async Task LoadAsync_sweeps_orphaned_dismissals_before_querying_the_report()
    {
        ReturnsReport(new DuplicateReport([], [], [], []));

        var vm = CreateVm();
        await vm.LoadAsync();

        Received.InOrder(() =>
        {
            _dispatcher.Send(Arg.Any<SweepOrphanedDismissals>(), Arg.Any<CancellationToken>());
            _dispatcher.Query(Arg.Any<GetDuplicateReport>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DismissAsync_sends_the_command_and_reloads()
    {
        ReturnsReport(new DuplicateReport([], [], [], []));

        var vm = CreateVm();
        await vm.LoadAsync();
        _dispatcher.ClearReceivedCalls();

        await vm.DismissAsync(DuplicateEntityType.Author, 2, 1, "note");

        // IDs should normalise (lower, higher) inside the handler; VM passes through as-is.
        await _dispatcher.Received(1).Send(new DismissDuplicatePair(DuplicateEntityType.Author, 2, 1, "note"), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Query(Arg.Any<GetDuplicateReport>(), Arg.Any<CancellationToken>());
        Assert.NotNull(vm.SuccessMessage);
    }

    [Fact]
    public async Task UnignoreAsync_sends_the_restore_command_and_reloads()
    {
        ReturnsReport(new DuplicateReport([], [], [], []));

        var vm = CreateVm();
        await vm.LoadAsync();
        _dispatcher.ClearReceivedCalls();

        await vm.UnignoreAsync(42);

        await _dispatcher.Received(1).Send(new RestoreDismissedDuplicate(42), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Query(Arg.Any<GetDuplicateReport>(), Arg.Any<CancellationToken>());
        Assert.NotNull(vm.SuccessMessage);
    }

    [Fact]
    public void ActiveCount_returns_zero_before_load()
    {
        var vm = CreateVm();
        Assert.Equal(0, vm.ActiveCount(DuplicateEntityType.Author));
        Assert.Empty(vm.ActiveAuthorPairs);
    }
}
