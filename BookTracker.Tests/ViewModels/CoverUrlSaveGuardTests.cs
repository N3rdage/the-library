using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Web.Services;
using BookTracker.Web.Validation;
using BookTracker.Web.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

/// <summary>
/// The save paths that take a cover URL refuse one the cover mirror cannot fetch
/// before sending anything through the dispatcher, so a substitute stands in for it.
/// The edit dialogs apply the rule only to a changed URL: a stored URL stays
/// saveable while unchanged.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class CoverUrlSaveGuardTests
{
    private const string HttpCover = "http://covers.example.org/x.jpg";

    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    [Fact]
    public async Task BookEditDialog_UnacceptableCoverUrl_DoesNotDispatch()
    {
        var vm = new BookEditDialogViewModel(_dispatcher) { Title = "Mort", CoverUrl = HttpCover };

        Assert.False(vm.CoverUrlAcceptable);
        await vm.SaveAsync();

        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Fact]
    public async Task BookEditDialog_UnchangedStoredUnacceptableCoverUrl_StillSaves()
    {
        // A mirrored cover in local dev is an Azurite blob URL on port 10000.
        const string stored = "http://localhost:10000/devstoreaccount1/book-covers/1.jpg";
        var vm = new BookEditDialogViewModel(_dispatcher) { Title = "Mort" };
        vm.LoadCoverUrl(stored);

        Assert.True(vm.CoverUrlAcceptable);
        Assert.Null(vm.CoverUrlError(vm.CoverUrl));
        await vm.SaveAsync();

        await _dispatcher.Received(1).Send(
            Arg.Is<UpdateBookDetails>(c => c!.CoverUrl == stored), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BookEditDialog_ChangedUnacceptableCoverUrl_IsRefused()
    {
        var vm = new BookEditDialogViewModel(_dispatcher) { Title = "Mort" };
        vm.LoadCoverUrl("http://localhost:10000/devstoreaccount1/book-covers/1.jpg");
        vm.CoverUrl = HttpCover;

        Assert.False(vm.CoverUrlAcceptable);
        Assert.Equal(CoverUrlAttribute.Message, vm.CoverUrlError(vm.CoverUrl));
        await vm.SaveAsync();

        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Fact]
    public async Task BookEditDialog_PaddedAcceptableCoverUrl_IsSavedTrimmed()
    {
        var vm = new BookEditDialogViewModel(_dispatcher)
        {
            Title = "Mort",
            CoverUrl = "  https://covers.example.org/x.jpg  ",
        };

        await vm.SaveAsync();

        await _dispatcher.Received(1).Send(
            Arg.Is<UpdateBookDetails>(c => c!.CoverUrl == "https://covers.example.org/x.jpg"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EditionFormDialog_StoredUnacceptableCoverUrl_IsAcceptableOnlyWhileUnchanged()
    {
        var vm = new EditionFormDialogViewModel(Substitute.For<IBookLookupService>(), _dispatcher);
        vm.LoadCoverUrl("https://localhost:10000/devstoreaccount1/book-covers/1.jpg");

        Assert.True(vm.CoverUrlAcceptable);
        Assert.Null(vm.CoverUrlError(" https://localhost:10000/devstoreaccount1/book-covers/1.jpg "));

        vm.CoverUrl = "https://localhost:10000/devstoreaccount1/book-covers/2.jpg";

        Assert.False(vm.CoverUrlAcceptable);
        Assert.Equal(CoverUrlAttribute.Message, vm.CoverUrlError(vm.CoverUrl));
    }

    [Fact]
    public async Task EditionFormDialog_UnacceptableCoverUrl_ReturnsNullWithoutDispatching()
    {
        var vm = new EditionFormDialogViewModel(Substitute.For<IBookLookupService>(), _dispatcher) { CoverUrl = HttpCover };

        Assert.False(vm.CoverUrlAcceptable);
        Assert.Null(await vm.SaveAsync());
        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Theory]
    [InlineData(HttpCover, null)]
    [InlineData(null, HttpCover)]
    public async Task BookAdd_UnacceptableCoverUrl_SetsTheFieldMessageBeforeSaving(string? bookCover, string? editionCover)
    {
        var vm = new BookAddViewModel(
            Substitute.For<IBookLookupService>(), new SeriesMatchService(_dispatcher),
            _dispatcher, NullLogger<BookAddViewModel>.Instance);
        vm.BookInput.Title = "Mort";
        vm.BookInput.DefaultCoverArtUrl = bookCover;
        vm.EditionInput.CoverUrl = editionCover;

        Assert.Null(await vm.SaveAsync([]));

        Assert.Equal(CoverUrlAttribute.Message, vm.Error);
        Assert.False(vm.Saving);
        Assert.Empty(_dispatcher.ReceivedCalls());
    }
}
