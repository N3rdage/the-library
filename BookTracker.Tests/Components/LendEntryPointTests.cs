using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Loans;
using BookTracker.Data.Models;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.ViewModels;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using NSubstitute;
using Xunit;
using BooksDetail = BookTracker.Web.Components.Pages.Books.Detail;
using LoansIndex = BookTracker.Web.Components.Pages.Loans.Index;

namespace BookTracker.Tests.Components;

// The ways into lending: the /loans "Lend a book" button (with and without any
// loans) and the Lend / Returned state in the book page header.
[Trait("Category", TestCategories.Component)]
public class LendEntryPointTests : ComponentTestBase
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private static readonly DateOnly Lent = new(2026, 11, 1);

    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public LendEntryPointTests()
    {
        Services.AddSingleton(_dispatcher);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(Substitute.For<IBookCoverStorage>());
        Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<CoverPhotoUploader>>(NullLogger<CoverPhotoUploader>.Instance);
        Services.AddTransient<CoverPhotoUploader>();
        Services.AddTransient<LoansViewModel>();
        Services.AddTransient<LendDialogViewModel>();
        Services.AddTransient<RemoveCopyDialogViewModel>();
        Services.AddTransient<BookDetailViewModel>();
    }

    private IRenderedComponent<LoansIndex> RenderLoans(params OpenLoanRow[] rows)
    {
        _dispatcher.Query(Arg.Any<GetOpenLoans>(), Arg.Any<CancellationToken>()).Returns(rows);
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        return Render<LoansIndex>();
    }

    private IRenderedComponent<BooksDetail> RenderDetail(params CopyDetail[] copies)
    {
        var detail = new BookDetail(
            7, "Dune", BookCategory.Fiction, BookStatus.Unread, 0, null, null, new DateTime(2026, 1, 1),
            Works: [],
            Editions: [new EditionDetail(3, "9780441013593", BookFormat.Hardcover, "Hardcover", null, "", null, false, copies)],
            Tags: [],
            Series: null);
        _dispatcher.Query(Arg.Any<GetBookDetail>(), Arg.Any<CancellationToken>()).Returns(detail);
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        return Render<BooksDetail>(p => p.Add(x => x.BookId, 7));
    }

    private static CopyDetail Free(int id) => new(id, BookCondition.Good, null, null);

    private static CopyDetail Out(int id, int loanId, string borrower) =>
        new(id, BookCondition.Good, null, null, new CopyLoanDetail(loanId, borrower, Lent, null));

    private static IEnumerable<string> ButtonTexts(IRenderedComponent<BooksDetail> cut) =>
        cut.FindAll("button.mud-button-filled-primary").Select(b => b.TextContent.Trim());

    [Fact]
    public void Loans_withNothingOut_explainsHowToLend_andShowsTheButton()
    {
        var cut = RenderLoans();

        Assert.Contains("Nothing is out on loan.", cut.Markup);
        Assert.Contains("Lend a book button", cut.Markup);
        Assert.Single(cut.FindAll("button"), b => b.TextContent.Trim() == "Lend a book");
    }

    [Fact]
    public void Loans_withLoansOut_stillShowsTheButton()
    {
        var cut = RenderLoans(new OpenLoanRow(1, 10, 100, "Dune", "Frank Herbert", null, "Sam", Lent, null));

        Assert.Single(cut.FindAll("button"), b => b.TextContent.Trim() == "Lend a book");
    }

    [Fact]
    public async Task Loans_lendButton_opensTheDialogEmpty_withConfirmDisabled()
    {
        var cut = RenderLoans();

        await cut.InvokeAsync(() => cut.Find("button.mud-button-filled-primary").Click());

        _dialogs.WaitForAssertion(() => Assert.Contains("Add at least one book to lend.", _dialogs.Markup));
        var confirm = _dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Lend");
        Assert.True(confirm.HasAttribute("disabled"));
    }

    [Fact]
    public void Detail_oneFreeCopy_showsLend()
    {
        var cut = RenderDetail(Free(11));

        Assert.Contains("Lend", ButtonTexts(cut));
        Assert.DoesNotContain("Lent to", cut.Markup);
    }

    private static IEnumerable<string> RowButtonTexts(IRenderedComponent<BooksDetail> cut) =>
        cut.FindAll("button.mud-button-text").Select(b => b.TextContent.Trim());

    [Fact]
    public void Detail_ownedCopy_offersRemove_butNotRestore()
    {
        var cut = RenderDetail(Free(11));

        Assert.Contains("Remove", RowButtonTexts(cut));
        Assert.DoesNotContain("Restore", RowButtonTexts(cut));
    }

    [Fact]
    public void Detail_lentCopy_hasNoRemove()
    {
        var cut = RenderDetail(Out(10, 1, "Ann"));

        Assert.DoesNotContain("Remove", RowButtonTexts(cut));
    }

    [Fact]
    public void Detail_removedCopy_showsReasonDateAndNote_offersRestore_andIsNotLendable()
    {
        var removed = new CopyDetail(12, BookCondition.Good, null, null, null,
            CopyDisposition.Donated, new DateOnly(2026, 11, 3), "To the school fete");

        var cut = RenderDetail(Free(11), removed);

        Assert.Contains("Donated · 3 Nov 2026", cut.Markup);
        Assert.Contains("To the school fete", cut.Markup);
        Assert.Contains("1 removed", cut.Markup);
        Assert.Single(RowButtonTexts(cut), t => t == "Restore");
        Assert.Single(RowButtonTexts(cut), t => t == "Lend");
        Assert.Single(RowButtonTexts(cut), t => t == "Remove");
    }

    [Fact]
    public void Detail_onlyRemovedCopies_hasNoHeaderLend()
    {
        var removed = new CopyDetail(12, BookCondition.Good, null, null, null,
            CopyDisposition.Lost, new DateOnly(2026, 11, 3), null);

        var cut = RenderDetail(removed);

        Assert.DoesNotContain("Lend", ButtonTexts(cut));
        Assert.DoesNotContain("Lend", RowButtonTexts(cut));
    }

    [Fact]
    public void Detail_noCopies_hasNoLendButton()
    {
        var cut = RenderDetail();

        Assert.DoesNotContain("Lend", ButtonTexts(cut));
        Assert.DoesNotContain("Returned", ButtonTexts(cut));
    }

    [Fact]
    public void Detail_everyCopyLent_showsWhoHasIt_andReturnedInPlaceOfLend()
    {
        var cut = RenderDetail(Out(10, 1, "Ann"), Out(11, 2, "Bob"));

        Assert.Contains("Lent to Ann, Bob", cut.Markup);
        Assert.DoesNotContain("Lend", ButtonTexts(cut));
        Assert.Contains("Returned", ButtonTexts(cut));
    }

    [Fact]
    public async Task Detail_headerReturned_returnsEveryOpenLoan()
    {
        var cut = RenderDetail(Out(10, 1, "Ann"), Out(11, 2, "Bob"));

        await cut.InvokeAsync(() => cut.Find("button.mud-button-filled-primary").Click());

        await _dispatcher.Received(1).Send(Arg.Is<ReturnLoan>(c => c!.LoanId == 1), Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).Send(Arg.Is<ReturnLoan>(c => c!.LoanId == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Detail_someCopiesFree_showsLend_notReturned()
    {
        var cut = RenderDetail(Out(10, 1, "Ann"), Free(11));

        Assert.Contains("Lend", ButtonTexts(cut));
        Assert.DoesNotContain("Returned", ButtonTexts(cut));
    }
}
