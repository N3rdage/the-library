using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Loans;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Components.Layout;
using BookTracker.Web.Components.Shared;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.ViewModels;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using BooksDetail = BookTracker.Web.Components.Pages.Books.Detail;

namespace BookTracker.Tests.Components;

// Where a refused action shows: a dialog puts the dispatching view model's Error
// in an alert above its buttons and stays open with the input intact; a page
// shows the same alert near its actions and raises no error snackbar.
[Trait("Category", TestCategories.Component)]
public class ActionErrorTests : ComponentTestBase
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    public ActionErrorTests()
    {
        Services.AddSingleton(_dispatcher);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(Substitute.For<IBookCoverStorage>());
        Services.AddSingleton<ILogger<CoverPhotoUploader>>(NullLogger<CoverPhotoUploader>.Instance);
        Services.AddTransient<CoverPhotoUploader>();
        Services.AddTransient<CopyFormDialogViewModel>();
        Services.AddTransient<LendDialogViewModel>();
        Services.AddTransient<RemoveCopyDialogViewModel>();
        Services.AddTransient<BookDetailViewModel>();
    }

    [Fact]
    public async Task Dialog_domainRuleFailure_showsAlertAboveTheButtons_andStaysOpen()
    {
        _dispatcher.Send(Arg.Any<AddCopyToEdition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainRuleException("Edition is full."));
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<CopyFormDialog> { { x => x.EditionId, (int?)5 }, { x => x.IsNew, true } };

        var reference = await dialogs.InvokeAsync(() => dialogService.ShowAsync<CopyFormDialog>("Add copy", parameters));
        dialogs.WaitForAssertion(() => Assert.Contains("Date acquired", dialogs.Markup));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click());

        dialogs.WaitForAssertion(() => Assert.Contains("Edition is full.", dialogs.Markup));
        var markup = dialogs.Markup;
        Assert.True(markup.IndexOf("Edition is full.", StringComparison.Ordinal)
            < markup.IndexOf("mud-dialog-actions", StringComparison.Ordinal));
        Assert.Contains("Date acquired", markup);
        Assert.False(reference.Result.IsCompleted);
    }

    [Fact]
    public async Task Page_refusedReturn_showsTheAlertAboveTheActions_andNoErrorSnackbar()
    {
        var copies = new[]
        {
            new CopyDetail(10, BookCondition.Good, null, null,
                new CopyLoanDetail(1, "Ann", new DateOnly(2026, 11, 1), null)),
        };
        var detail = new BookDetail(
            7, "Dune", BookCategory.Fiction, BookStatus.Unread, 0, null, null, new DateTime(2026, 1, 1),
            Works: [],
            Editions: [new EditionDetail(3, "9780441013593", BookFormat.Hardcover, "Hardcover", null, "", null, false, copies)],
            Tags: [],
            Series: null);
        _dispatcher.Query(Arg.Any<GetBookDetail>(), Arg.Any<CancellationToken>()).Returns(detail);
        _dispatcher.Send(Arg.Any<ReturnLoan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("That loan no longer exists."));
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        var cut = Render<BooksDetail>(p => p.Add(x => x.BookId, 7));

        await cut.InvokeAsync(() => cut.Find("button.mud-button-filled-primary").Click());

        cut.WaitForAssertion(() => Assert.Contains("That loan no longer exists.", cut.Markup));
        Assert.True(cut.Markup.IndexOf("That loan no longer exists.", StringComparison.Ordinal)
            < cut.Markup.IndexOf("Lent to Ann", StringComparison.Ordinal));
        Assert.Empty(Services.GetRequiredService<ISnackbar>().ShownSnackbars);
    }

    [Fact]
    public void Page_debouncedNotesSaveFault_reachesTheErrorBoundary()
    {
        var detail = new BookDetail(
            7, "Dune", BookCategory.Fiction, BookStatus.Unread, 0, null, null, new DateTime(2026, 1, 1),
            Works: [], Editions: [], Tags: [], Series: null);
        _dispatcher.Query(Arg.Any<GetBookDetail>(), Arg.Any<CancellationToken>()).Returns(detail);
        _dispatcher.Send(Arg.Any<UpdateBookNotes>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("notes store down"));
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        var cut = Render<PageErrorBoundary>(p => p
            .Add(b => b.ChildContent, (RenderFragment)(b =>
            {
                b.OpenComponent<BooksDetail>(0);
                b.AddComponentParameter(1, nameof(BooksDetail.BookId), 7);
                b.CloseComponent();
            }))
            .Add(b => b.ErrorContent, (RenderFragment<Exception>)(ex => b => b.AddContent(0, $"fault: {ex.Message}"))));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("textarea")));
        cut.Find("textarea").Input("A note");

        cut.WaitForAssertion(() => Assert.Contains("fault: notes store down", cut.Markup), TimeSpan.FromSeconds(5));
    }
}
