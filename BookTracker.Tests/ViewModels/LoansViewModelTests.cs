using BookTracker.Application;
using BookTracker.Application.Loans;
using BookTracker.Data;
using BookTracker.Web.ViewModels;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// Unit tests for the /loans VM: grouping, the overdue flag, return-in-place and
// the failure path. The queries themselves are covered by LoanQueryHandlersTests
// and GetLoanHistoryHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class LoansViewModelTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // 01:00 UTC on 19 Nov is 12:00 on 19 Nov in Melbourne (AEDT, UTC+11).
    private static readonly DateOnly Today = new(2026, 11, 19);
    private readonly TimeProvider _clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 1, 0, 0, TimeSpan.Zero));

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static OpenLoanRow Row(int loanId, string borrower, DateOnly? dueOn = null) =>
        new(loanId, CopyId: loanId * 10, BookId: loanId * 100, $"Book {loanId}", "Author", null,
            borrower, Today.AddDays(-30), dueOn);

    private void StubOpenLoans(params OpenLoanRow[] rows) =>
        _dispatcher.Query(Arg.Any<GetOpenLoans>(), Arg.Any<CancellationToken>()).Returns(rows);

    private async Task<LoansViewModel> LoadedVm(params OpenLoanRow[] rows)
    {
        StubOpenLoans(rows);
        var vm = new LoansViewModel(_dispatcher, _clock);
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task LoadAsync_groupsByBorrower_preservingQueryOrder()
    {
        var vm = await LoadedVm(Row(1, "Alex"), Row(2, "Sam"), Row(3, "Sam"), Row(4, "Zoe"));

        Assert.True(vm.Loaded);
        Assert.Equal(new[] { "Alex", "Sam", "Zoe" }, vm.Groups.Select(g => g.BorrowerName));
        Assert.Equal(new[] { 2, 3 }, vm.Groups[1].Loans.Select(l => l.LoanId));
        Assert.Equal(Today, vm.Today);
    }

    [Fact]
    public async Task LoadAsync_noLoans_leavesNoGroups()
    {
        var vm = await LoadedVm();

        Assert.True(vm.Loaded);
        Assert.Empty(vm.Groups);
    }

    [Fact]
    public async Task IsOverdue_dueToday_isFalse_dueYesterday_isTrue_noDueDate_isFalse()
    {
        var dueToday = Row(1, "Sam", Today);
        var dueYesterday = Row(2, "Sam", Today.AddDays(-1));
        var noDueDate = Row(3, "Sam");
        var vm = await LoadedVm(dueToday, dueYesterday, noDueDate);

        Assert.False(vm.IsOverdue(dueToday));
        Assert.True(vm.IsOverdue(dueYesterday));
        Assert.False(vm.IsOverdue(noDueDate));
    }

    [Fact]
    public async Task ReturnAsync_dispatchesTodaysDate_andRemovesTheRowInPlace()
    {
        var vm = await LoadedVm(Row(1, "Alex"), Row(2, "Sam"), Row(3, "Sam"));

        await vm.ReturnAsync(2);

        await _dispatcher.Received(1).Send(new ReturnLoan(2, Today), Arg.Any<CancellationToken>());
        Assert.Equal(new[] { 3 }, vm.Groups.Single(g => g.BorrowerName == "Sam").Loans.Select(l => l.LoanId));
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(vm.ReturningLoanIds);
        // No reload: the open-loans query ran once, for the initial load.
        await _dispatcher.Received(1).Query(Arg.Any<GetOpenLoans>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnAsync_lastLoanOfABorrower_dropsTheGroup()
    {
        var vm = await LoadedVm(Row(1, "Alex"), Row(2, "Sam"));

        await vm.ReturnAsync(1);

        Assert.Equal(new[] { "Sam" }, vm.Groups.Select(g => g.BorrowerName));
    }

    [Fact]
    public async Task ReturnAsync_domainRuleFailure_showsMessage_andReloads()
    {
        var vm = await LoadedVm(Row(1, "Alex"), Row(2, "Sam"));
        _dispatcher.Send(Arg.Any<ReturnLoan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("This loan has already been returned."));
        StubOpenLoans(Row(2, "Sam"));

        await vm.ReturnAsync(1);

        Assert.Equal("This loan has already been returned.", vm.ErrorMessage);
        Assert.Equal(new[] { "Sam" }, vm.Groups.Select(g => g.BorrowerName));
        Assert.Empty(vm.ReturningLoanIds);
        await _dispatcher.Received(2).Query(Arg.Any<GetOpenLoans>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnAsync_notFound_showsMessage_andReloads()
    {
        var vm = await LoadedVm(Row(1, "Alex"));
        _dispatcher.Send(Arg.Any<ReturnLoan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("Loan 1 not found."));
        StubOpenLoans();

        await vm.ReturnAsync(1);

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Empty(vm.Groups);
        await _dispatcher.Received(2).Query(Arg.Any<GetOpenLoans>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureHistoryLoadedAsync_queriesOnce()
    {
        var vm = await LoadedVm();
        _dispatcher.Query(Arg.Any<GetLoanHistory>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new LoanHistoryRow(9, 90, "Dune", "Sam", Today.AddDays(-20), Today.AddDays(-2)) });

        await vm.EnsureHistoryLoadedAsync();
        await vm.EnsureHistoryLoadedAsync();

        Assert.True(vm.HistoryLoaded);
        Assert.Equal("Dune", Assert.Single(vm.History).Title);
        await _dispatcher.Received(1).Query(Arg.Any<GetLoanHistory>(), Arg.Any<CancellationToken>());
    }
}
