using BookTracker.Application;
using BookTracker.Application.Home;
using BookTracker.Application.Loans;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// Thin unit tests for the VM-side of the home dashboard: it copies the
// GetHomeDashboard projection onto its surface and derives the bar-scaling
// maxima, and carries the GetDueBack panel with its overdue flag. The DbContext
// reads are covered by GetHomeDashboardHandlerTests and GetDueBackHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class HomeViewModelTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // 01:00 UTC on 19 Nov is 12:00 on 19 Nov in Melbourne (AEDT, UTC+11).
    private static readonly DateOnly Today = new(2026, 11, 19);
    private readonly TimeProvider _clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 1, 0, 0, TimeSpan.Zero));

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static readonly HomeDashboard EmptyDashboard = new(0, 0, [], []);

    private HomeViewModel CreateVm(HomeDashboard dashboard, DueBackSummary? dueBack = null)
    {
        _dispatcher.Query(Arg.Any<GetHomeDashboard>(), Arg.Any<CancellationToken>())
            .Returns(dashboard);
        _dispatcher.Query(Arg.Any<GetDueBack>(), Arg.Any<CancellationToken>())
            .Returns(dueBack ?? new DueBackSummary([], 0));
        return new HomeViewModel(_dispatcher, _clock);
    }

    private static DueBackRow Row(int id, DateOnly dueOn) =>
        new(id, BookId: id * 100, $"Book {id}", "Sam", dueOn);

    [Fact]
    public async Task InitializeAsync_EmptyDashboard_ReturnsZeroCounts()
    {
        var vm = CreateVm(EmptyDashboard);

        await vm.InitializeAsync();

        Assert.Equal(0, vm.TotalBooks);
        Assert.Equal(0, vm.TotalAuthors);
        Assert.Empty(vm.TopAuthors);
        Assert.Empty(vm.TopGenres);
        Assert.Equal(0, vm.MaxAuthor);
        Assert.Equal(0, vm.MaxGenre);
    }

    [Fact]
    public async Task InitializeAsync_CopiesProjection_AndDerivesMaxima()
    {
        var vm = CreateVm(new HomeDashboard(
            TotalBooks: 3,
            TotalAuthors: 2,
            TopAuthors: [new AuthorCount(1, "Author 1", 2), new AuthorCount(2, "Author 2", 1)],
            TopGenres: [new GenreCount("Fantasy", 5), new GenreCount("Sci-Fi", 2)]));

        await vm.InitializeAsync();

        Assert.Equal(3, vm.TotalBooks);
        Assert.Equal(2, vm.TotalAuthors);
        Assert.Equal(2, vm.TopAuthors.Count);
        Assert.Equal("Author 1", vm.TopAuthors[0].Author);
        Assert.Equal(2, vm.MaxAuthor);   // max of {2, 1}
        Assert.Equal(5, vm.MaxGenre);     // max of {5, 2}
    }

    [Fact]
    public async Task InitializeAsync_NothingLent_HidesTheDueBackPanel()
    {
        var vm = CreateVm(EmptyDashboard);

        await vm.InitializeAsync();

        Assert.False(vm.ShowDueBack);
        Assert.Empty(vm.DueBack);
        Assert.Equal(0, vm.OpenEndedLoanCount);
        await _dispatcher.Received(1).Query(Arg.Any<GetDueBack>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeAsync_LoadsTheDueBackRows_AndFlagsOverdueFromTheDayAfter()
    {
        var overdue = Row(1, Today.AddDays(-1));
        var dueToday = Row(2, Today);
        var later = Row(3, Today.AddDays(5));
        var vm = CreateVm(EmptyDashboard, new DueBackSummary([overdue, dueToday, later], 0));

        await vm.InitializeAsync();

        Assert.True(vm.ShowDueBack);
        Assert.Equal(Today, vm.Today);
        Assert.Equal(new[] { overdue, dueToday, later }, vm.DueBack);
        Assert.True(vm.IsOverdue(overdue));
        Assert.False(vm.IsOverdue(dueToday));
        Assert.False(vm.IsOverdue(later));
    }

    [Fact]
    public async Task InitializeAsync_OnlyOpenEndedLoans_StillShowsThePanel()
    {
        var vm = CreateVm(EmptyDashboard, new DueBackSummary([], 3));

        await vm.InitializeAsync();

        Assert.True(vm.ShowDueBack);
        Assert.Empty(vm.DueBack);
        Assert.Equal(3, vm.OpenEndedLoanCount);
    }
}
