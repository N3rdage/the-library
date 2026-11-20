using BookTracker.Application;
using BookTracker.Application.Loans;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookTracker.Tests.ViewModels;

// The lend dialog reads through GetBorrowerNames / SearchLendableBooks and
// writes through LendCopies; a substituted dispatcher stands in for all three.
// The SQL is covered by LoanQueryHandlersTests and SearchLendableBooksHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class LendDialogViewModelTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    // 14:00 UTC on 19 Nov is 20 Nov in Melbourne.
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 14, 0, 0, TimeSpan.Zero));
    private static readonly DateTime Today = new(2026, 11, 20);

    private static readonly LendDialogViewModel.LendCopyOption First =
        new(11, "Dune", "Hardcover · 9780441013593", BookCondition.Good);

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private LendDialogViewModel InitializedVm()
    {
        var vm = new LendDialogViewModel(_dispatcher, Clock);
        vm.Initialize(First);
        return vm;
    }

    private void GivenBooks(params LendableBook[] books) =>
        _dispatcher.Query(Arg.Any<SearchLendableBooks>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<LendableBook>)books);

    [Fact]
    public void Initialize_PreselectsTheCopy_AndLentOnIsToday()
    {
        var vm = InitializedVm();

        Assert.Equal([First], vm.Copies);
        Assert.Equal(Today, vm.LentOn);
        Assert.Null(vm.DueOn);
        Assert.Null(vm.BorrowerName);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.CanConfirm); // no borrower yet
    }

    [Fact]
    public void InitializeEmpty_startsWithNoCopies_andCannotConfirmUntilOneIsAdded()
    {
        var vm = new LendDialogViewModel(_dispatcher, Clock);
        vm.Initialize();
        vm.BorrowerName = "Sam";

        Assert.Empty(vm.Copies);
        Assert.Equal(Today, vm.LentOn);
        Assert.False(vm.CanConfirm);

        vm.AddCopy(First);

        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void InitializeEmpty_clearsAPreviousSession()
    {
        var vm = InitializedVm();
        vm.BorrowerName = "Sam";

        vm.Initialize();

        Assert.Empty(vm.Copies);
        Assert.Null(vm.BorrowerName);
    }

    [Fact]
    public void LentMessage_namesTheBorrower_orCountsTheCopies()
    {
        var vm = InitializedVm();
        vm.BorrowerName = "  Sam ";
        Assert.Equal("Lent to Sam", vm.LentMessage);

        vm.AddCopy(new LendDialogViewModel.LendCopyOption(12, "Emma", "Paperback", BookCondition.Good));
        Assert.Equal("2 copies lent to Sam", vm.LentMessage);
    }

    [Fact]
    public async Task SuggestBorrowersAsync_QueriesGetBorrowerNamesWithThePrefix()
    {
        _dispatcher.Query(Arg.Is<GetBorrowerNames>(q => q!.Prefix == "sa"), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)["Sam", "Sarah"]);
        var vm = InitializedVm();

        var names = await vm.SuggestBorrowersAsync("sa", CancellationToken.None);

        Assert.Equal(["Sam", "Sarah"], names);
    }

    [Fact]
    public async Task SearchCopiesAsync_OffersEachFreeCopy_LeavingOutCopiesAlreadyListed()
    {
        GivenBooks(
            new LendableBook(1, "Dune", "Frank Herbert",
                [new LendableCopy(11, "Hardcover · 9780441013593", BookCondition.Good),
                 new LendableCopy(12, "Mass Market Paperback · no ISBN", BookCondition.Fair)]),
            new LendableBook(2, "Dune Messiah", "Frank Herbert",
                [new LendableCopy(21, "Hardcover · no ISBN", BookCondition.Fine)]));
        var vm = InitializedVm();

        var options = (await vm.SearchCopiesAsync("dune", CancellationToken.None)).ToList();

        Assert.Equal([12, 21], options.Select(o => o.CopyId));
        Assert.Equal("Dune — Mass Market Paperback · no ISBN (Fair)", options[0].Display);
        await _dispatcher.Received(1).Query(Arg.Is<SearchLendableBooks>(q => q!.Query == "dune"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchCopiesAsync_BlankQuery_DoesNotQuery()
    {
        var vm = InitializedVm();

        Assert.Empty(await vm.SearchCopiesAsync("  ", CancellationToken.None));
        await _dispatcher.DidNotReceive().Query(Arg.Any<SearchLendableBooks>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddCopy_AppendsOnce_AndRemoveCopyTakesItOff()
    {
        var vm = InitializedVm();
        var second = new LendDialogViewModel.LendCopyOption(21, "Dune Messiah", "Hardcover · no ISBN", BookCondition.Fine);

        vm.AddCopy(second);
        vm.AddCopy(second);
        Assert.Equal([11, 21], vm.Copies.Select(c => c.CopyId));

        vm.RemoveCopy(11);
        Assert.Equal([21], vm.Copies.Select(c => c.CopyId));

        vm.RemoveCopy(21);
        vm.BorrowerName = "Sam";
        Assert.False(vm.CanConfirm); // nothing left to lend
    }

    [Fact]
    public async Task ConfirmAsync_SendsOneLendCopies_WithEveryId_TrimmedName_AndDates()
    {
        var vm = InitializedVm();
        vm.AddCopy(new LendDialogViewModel.LendCopyOption(21, "Dune Messiah", "Hardcover · no ISBN", BookCondition.Fine));
        vm.BorrowerName = "  Sam  ";
        vm.LentOn = new DateTime(2026, 11, 18);
        vm.DueOn = new DateTime(2026, 12, 18);

        var ok = await vm.ConfirmAsync();

        Assert.True(ok);
        await _dispatcher.Received(1).Send(
            Arg.Is<LendCopies>(c =>
                c!.CopyIds.SequenceEqual(new[] { 11, 21 })
                && c.BorrowerName == "Sam"
                && c.LentOn == new DateOnly(2026, 11, 18)
                && c.DueOn == new DateOnly(2026, 12, 18)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_NoDueDate_SendsNullDueOn()
    {
        var vm = InitializedVm();
        vm.BorrowerName = "Sam";

        Assert.True(await vm.ConfirmAsync());

        await _dispatcher.Received(1).Send(
            Arg.Is<LendCopies>(c => c!.DueOn == null && c.LentOn == DateOnly.FromDateTime(Today)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_DomainRuleRefusal_KeepsTheDialogOpenWithTheMessage()
    {
        _dispatcher.Send(Arg.Any<LendCopies>(), Arg.Any<CancellationToken>())
            .Throws(new DomainRuleException("This copy is already lent out."));
        var vm = InitializedVm();
        vm.BorrowerName = "Sam";

        var ok = await vm.ConfirmAsync();

        Assert.False(ok);
        Assert.Equal("This copy is already lent out.", vm.ErrorMessage);
        Assert.False(vm.Saving);
        Assert.Equal([First], vm.Copies);
    }

    [Fact]
    public async Task ConfirmAsync_MissingCopy_ShowsTheNotFoundMessage()
    {
        _dispatcher.Send(Arg.Any<LendCopies>(), Arg.Any<CancellationToken>())
            .Throws(new NotFoundException("Copy 11 not found."));
        var vm = InitializedVm();
        vm.BorrowerName = "Sam";

        Assert.False(await vm.ConfirmAsync());
        Assert.Equal("Copy 11 not found.", vm.ErrorMessage);
    }

    [Fact]
    public async Task ConfirmAsync_BlankName_DoesNotSend()
    {
        var vm = InitializedVm();
        vm.BorrowerName = "   ";

        Assert.False(await vm.ConfirmAsync());
        await _dispatcher.DidNotReceive().Send(Arg.Any<LendCopies>(), Arg.Any<CancellationToken>());
    }
}
