using BookTracker.Data.Models;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

public class DuplicatesViewModelTests
{
    private readonly IDuplicateDetectionService _detector = Substitute.For<IDuplicateDetectionService>();

    private DuplicatesViewModel CreateVm() => new(_detector);

    private static AuthorDuplicatePair ActiveAuthorPair(int a, int b) =>
        new(new AuthorSnapshot(a, $"Author {a}", 0, null, null),
            new AuthorSnapshot(b, $"Author {b}", 0, null, null),
            null, "test");

    private static AuthorDuplicatePair DismissedAuthorPair(int a, int b, int ignoredId) =>
        new(new AuthorSnapshot(a, $"Author {a}", 0, null, null),
            new AuthorSnapshot(b, $"Author {b}", 0, null, null),
            new DismissalInfo(ignoredId, DateTime.UtcNow, null),
            "test");

    [Fact]
    public async Task LoadAsync_populates_active_and_dismissed_splits_per_tab()
    {
        _detector.DetectAllAsync(Arg.Any<CancellationToken>()).Returns(new DuplicateReport(
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
    public async Task DismissAsync_calls_service_and_reloads()
    {
        _detector.DetectAllAsync(Arg.Any<CancellationToken>()).Returns(new DuplicateReport([], [], [], []));

        var vm = CreateVm();
        await vm.LoadAsync();
        _detector.ClearReceivedCalls();

        await vm.DismissAsync(DuplicateEntityType.Author, 2, 1, "note");

        // IDs should normalise (lower, higher) inside the service; VM passes through as-is.
        await _detector.Received(1).DismissAsync(DuplicateEntityType.Author, 2, 1, "note", Arg.Any<CancellationToken>());
        await _detector.Received(1).DetectAllAsync(Arg.Any<CancellationToken>());
        Assert.NotNull(vm.SuccessMessage);
    }

    [Fact]
    public async Task UnignoreAsync_calls_service_and_reloads()
    {
        _detector.DetectAllAsync(Arg.Any<CancellationToken>()).Returns(new DuplicateReport([], [], [], []));

        var vm = CreateVm();
        await vm.LoadAsync();
        _detector.ClearReceivedCalls();

        await vm.UnignoreAsync(42);

        await _detector.Received(1).UnignoreAsync(42, Arg.Any<CancellationToken>());
        await _detector.Received(1).DetectAllAsync(Arg.Any<CancellationToken>());
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
