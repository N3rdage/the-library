using BookTracker.Application;
using BookTracker.Application.Home;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// Thin unit tests for the VM-side of the home dashboard: it copies the
// GetHomeDashboard projection onto its surface and derives the bar-scaling
// maxima. The DbContext read itself is covered by GetHomeDashboardHandlerTests.
[Trait("Category", TestCategories.Unit)]
public class HomeViewModelTests
{
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private HomeViewModel CreateVm(HomeDashboard dashboard)
    {
        _dispatcher.Query(Arg.Any<GetHomeDashboard>(), Arg.Any<CancellationToken>())
            .Returns(dashboard);
        return new HomeViewModel(_dispatcher);
    }

    [Fact]
    public async Task InitializeAsync_EmptyDashboard_ReturnsZeroCounts()
    {
        var vm = CreateVm(new HomeDashboard(0, 0, [], []));

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
}
