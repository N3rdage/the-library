using BookTracker.Application;
using BookTracker.Data;
using BookTracker.Web.Services;
using BookTracker.Web.Validation;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

/// <summary>
/// The save paths that take a cover URL refuse one the cover mirror cannot fetch
/// before touching the database or the dispatcher, so substitutes stand in for both.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class CoverUrlSaveGuardTests
{
    private const string HttpCover = "http://covers.example.org/x.jpg";

    private readonly IDbContextFactory<BookTrackerDbContext> _dbFactory = Substitute.For<IDbContextFactory<BookTrackerDbContext>>();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    [Fact]
    public async Task BookEditDialog_UnacceptableCoverUrl_DoesNotDispatch()
    {
        var vm = new BookEditDialogViewModel(_dbFactory, _dispatcher) { Title = "Mort", CoverUrl = HttpCover };

        Assert.False(vm.CoverUrlAcceptable);
        await vm.SaveAsync();

        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Fact]
    public async Task EditionFormDialog_UnacceptableCoverUrl_ReturnsNullWithoutDispatching()
    {
        var vm = new EditionFormDialogViewModel(_dbFactory, Substitute.For<IBookLookupService>(), _dispatcher) { CoverUrl = HttpCover };

        Assert.False(vm.CoverUrlAcceptable);
        Assert.Null(await vm.SaveAsync());
        Assert.Empty(_dispatcher.ReceivedCalls());
    }

    [Theory]
    [InlineData(HttpCover, null)]
    [InlineData(null, HttpCover)]
    public async Task BookAdd_UnacceptableCoverUrl_ThrowsTheFieldMessageBeforeSaving(string? bookCover, string? editionCover)
    {
        var vm = new BookAddViewModel(
            _dbFactory, Substitute.For<IBookLookupService>(), new SeriesMatchService(_dbFactory),
            new WorkSearchService(_dbFactory), _dispatcher, NullLogger<BookAddViewModel>.Instance);
        vm.BookInput.Title = "Mort";
        vm.BookInput.DefaultCoverArtUrl = bookCover;
        vm.EditionInput.CoverUrl = editionCover;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.SaveAsync([]));

        Assert.Equal(CoverUrlAttribute.Message, ex.Message);
        Assert.False(vm.Saving);
        Assert.Empty(_dbFactory.ReceivedCalls());
        Assert.Empty(_dispatcher.ReceivedCalls());
    }
}
