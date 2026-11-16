using BookTracker.Application;
using BookTracker.Application.Works;
using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using NSubstitute;

namespace BookTracker.Tests.ViewModels;

// The dialog reads through GetWorkForEdit and writes through UpdateWork; a
// substituted dispatcher stands in for both. The read's SQL (author/contributor
// split and order, genre ids) is covered by GetWorkForEditHandlerTests; the
// save's persistence (title trim, author find-or-create, genre reconcile) by
// WorkCommandHandlersTests.
[Trait("Category", TestCategories.Unit)]
public class WorkEditDialogViewModelTests
{
    private const int WorkId = 9;
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private void GivenWork(WorkForEdit? work) =>
        _dispatcher.Query(Arg.Is<GetWorkForEdit>(q => q!.WorkId == WorkId), Arg.Any<CancellationToken>())
            .Returns(work);

    private static WorkForEdit Work(
        string title = "w", string? subtitle = null, string[]? authors = null,
        WorkContributorView[]? contributors = null, DateOnly? firstPublished = null,
        DatePrecision precision = DatePrecision.Day, int[]? genreIds = null) =>
        new(WorkId, title, subtitle, authors ?? ["a"], contributors ?? [],
            firstPublished, precision, genreIds ?? []);

    private UpdateWork? SentUpdate() =>
        _dispatcher.ReceivedCalls()
            .Select(c => c.GetArguments()[0])
            .OfType<UpdateWork>()
            .SingleOrDefault();

    [Fact]
    public async Task InitializeAsync_MissingId_MarksNotFound()
    {
        GivenWork(null);
        var vm = new WorkEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(WorkId);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeAsync_LoadsCurrentValues()
    {
        GivenWork(Work("Mort", "A Discworld Novel", ["Terry Pratchett"],
            firstPublished: new DateOnly(1987, 11, 12), precision: DatePrecision.Day));
        var vm = new WorkEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(WorkId);

        Assert.False(vm.NotFound);
        Assert.Equal(WorkId, vm.WorkId);
        Assert.Equal("Mort", vm.Title);
        Assert.Equal("A Discworld Novel", vm.Subtitle);
        Assert.Equal(new[] { "Terry Pratchett" }, vm.AuthorNames);
        Assert.Equal("12 Nov 1987", vm.FirstPublishedDate);
    }

    [Fact]
    public async Task InitializeAsync_LoadsContributorsInReadOrder()
    {
        GivenWork(Work(contributors:
        [
            new WorkContributorView("Amy Editor", AuthorRole.Editor),
            new WorkContributorView("Ann Translator", AuthorRole.Translator),
        ]));
        var vm = new WorkEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(WorkId);

        Assert.Equal(["Amy Editor", "Ann Translator"], vm.Contributors.Select(c => c.Name));
        Assert.Equal([AuthorRole.Editor, AuthorRole.Translator], vm.Contributors.Select(c => c.Role));
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingGenreIds()
    {
        const int fantasyId = 4;
        GivenWork(Work(genreIds: [fantasyId]));
        var vm = new WorkEditDialogViewModel(_dispatcher);

        await vm.InitializeAsync(WorkId);

        Assert.Single(vm.SelectedGenreIds);
        Assert.Contains(fantasyId, vm.SelectedGenreIds);
    }

    [Fact]
    public async Task InitializeAsync_ListsAreMutableCopies()
    {
        // The chip pickers and genre picker mutate these lists in place.
        var work = Work(authors: ["Terry Pratchett"], genreIds: [4]);
        GivenWork(work);
        var vm = new WorkEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(WorkId);

        vm.AuthorNames.Add("Neil Gaiman");
        vm.SelectedGenreIds.Add(5);

        Assert.Single(work.AuthorNames);
        Assert.Single(work.GenreIds);
    }

    [Fact]
    public async Task SaveAsync_SendsBasicFields()
    {
        GivenWork(Work("Old", authors: ["Old Author"]));
        var vm = new WorkEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(WorkId);
        vm.Title = "  Mort  ";
        vm.Subtitle = "A Discworld Novel";
        vm.AuthorNames = ["Old Author"];
        vm.FirstPublishedDate = "1987";

        await vm.SaveAsync();

        var sent = SentUpdate();
        Assert.NotNull(sent);
        Assert.Equal(WorkId, sent.WorkId);
        Assert.Equal("  Mort  ", sent.Title); // the handler trims
        Assert.Equal("A Discworld Novel", sent.Subtitle);
        Assert.Equal(["Old Author"], sent.AuthorNames);
        Assert.Equal(new DateOnly(1987, 1, 1), sent.FirstPublished);
        Assert.Equal(DatePrecision.Year, sent.Precision);
    }

    [Fact]
    public async Task SaveAsync_SendsContributorsWithNames_DroppingBlankRows()
    {
        GivenWork(Work());
        var vm = new WorkEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(WorkId);
        vm.Contributors = [new ContributorEntry("Doug Mauss", AuthorRole.Editor), new ContributorEntry(" ", AuthorRole.Translator)];

        await vm.SaveAsync();

        var sent = SentUpdate();
        Assert.NotNull(sent);
        Assert.Equal([new ContributorInput("Doug Mauss", AuthorRole.Editor)], sent.Contributors);
    }

    [Fact]
    public async Task SaveAsync_SendsGenreSelection()
    {
        const int fantasyId = 4, horrorId = 5;
        GivenWork(Work(genreIds: [fantasyId]));
        var vm = new WorkEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(WorkId);
        // Swap selection: drop Fantasy, add Horror.
        vm.SelectedGenreIds = [horrorId];

        await vm.SaveAsync();

        var sent = SentUpdate();
        Assert.NotNull(sent);
        Assert.Equal([horrorId], sent.GenreIds);
    }

    [Fact]
    public async Task SaveAsync_BlankTitleOrNoAuthor_IsNoOp()
    {
        GivenWork(Work());
        var vm = new WorkEditDialogViewModel(_dispatcher);
        await vm.InitializeAsync(WorkId);

        vm.Title = "   ";
        await vm.SaveAsync();
        vm.Title = "Mort";
        vm.AuthorNames = [" "];
        await vm.SaveAsync();

        Assert.Null(SentUpdate());
    }
}
