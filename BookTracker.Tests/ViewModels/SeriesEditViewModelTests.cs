using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests.ViewModels;

// First coverage for SeriesEditViewModel — added alongside the PR3b adoption
// (the VM now dispatches the Series command handlers instead of touching EF
// directly). These guard the page contract: signatures unchanged, the in-memory
// Works list stays in sync, and the new friendly duplicate-name error surfaces.
[Trait("Category", TestCategories.Integration)]
public class SeriesEditViewModelTests
{
    private readonly TestDbContextFactory _factory = new();

    private SeriesEditViewModel NewVm() => new(_factory, TestDispatcher.For(_factory));

    private async Task<int> SeedSeriesAsync(string name = "Discworld", SeriesType type = SeriesType.Series)
    {
        await using var db = _factory.CreateDbContext();
        var s = new Series { Name = name, Type = type };
        db.Series.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    private async Task<int> SeedWorkAsync(string title, int? seriesId = null, int? order = null, string? orderDisplay = null)
    {
        await using var db = _factory.CreateDbContext();
        var work = new Work
        {
            Title = title,
            WorkAuthors = { new WorkAuthor { Author = new Author { Name = $"Author of {title}" }, Order = 0, Role = AuthorRole.Author } },
            SeriesId = seriesId,
            SeriesOrder = order,
            SeriesOrderDisplay = orderDisplay,
        };
        db.Books.Add(new Book { Title = title, Works = { work } });
        await db.SaveChangesAsync();
        return work.Id;
    }

    // --- Initialize ----------------------------------------------------------

    [Fact]
    public void InitializeNew_setsIsNew_andEmptyInput()
    {
        var vm = NewVm();
        vm.InitializeNew();

        Assert.True(vm.IsNew);
        Assert.NotNull(vm.Input);
        Assert.Null(vm.Input!.Name);
    }

    [Fact]
    public async Task InitializeAsync_missing_marksNotFound()
    {
        var vm = NewVm();
        await vm.InitializeAsync(999999);
        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeAsync_loadsDetails_andWorksInOrder()
    {
        var seriesId = await SeedSeriesAsync("Foundation");
        await SeedWorkAsync("Second Foundation", seriesId, order: 2);
        await SeedWorkAsync("Foundation", seriesId, order: 1);

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);

        Assert.False(vm.NotFound);
        Assert.Equal("Foundation", vm.Input!.Name);
        Assert.Equal(2, vm.Works.Count);
        Assert.Equal("Foundation", vm.Works[0].Title);       // order 1 first
        Assert.Equal("Second Foundation", vm.Works[1].Title);
    }

    // --- SaveAsync (create) --------------------------------------------------

    [Fact]
    public async Task SaveAsync_create_persistsAndReturnsId()
    {
        var vm = NewVm();
        vm.InitializeNew();
        vm.Input!.Name = "  Foundation  ";
        vm.Input.Author = "Isaac Asimov";
        vm.Input.Type = SeriesType.Series;
        vm.Input.ExpectedCount = 7;

        var id = await vm.SaveAsync(null);

        Assert.NotNull(id);
        await using var db = _factory.CreateDbContext();
        var saved = await db.Series.FindAsync(id);
        Assert.Equal("Foundation", saved!.Name);   // aggregate trimmed
        Assert.Equal(7, saved.ExpectedCount);
    }

    [Fact]
    public async Task SaveAsync_create_duplicateName_setsError_returnsNull_persistsNothing()
    {
        await SeedSeriesAsync("Discworld");

        var vm = NewVm();
        vm.InitializeNew();
        vm.Input!.Name = "discworld"; // case-insensitive clash

        var id = await vm.SaveAsync(null);

        Assert.Null(id);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.Series.CountAsync()); // nothing new created
    }

    // --- SaveAsync (edit) ----------------------------------------------------

    [Fact]
    public async Task SaveAsync_edit_updates_andSetsSuccess()
    {
        var seriesId = await SeedSeriesAsync("Old Name");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        vm.Input!.Name = "New Name";
        vm.Input.Author = "New Author";

        var id = await vm.SaveAsync(seriesId);

        Assert.Equal(seriesId, id);
        Assert.False(string.IsNullOrEmpty(vm.SuccessMessage));
        await using var db = _factory.CreateDbContext();
        Assert.Equal("New Name", (await db.Series.FindAsync(seriesId))!.Name);
    }

    [Fact]
    public async Task SaveAsync_edit_missing_marksNotFound_returnsNull()
    {
        var seriesId = await SeedSeriesAsync("Doomed");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId); // IsNew=false, Input populated
        vm.Input!.Name = "Renamed";

        // Series deleted out from under the editor between load and save.
        await using (var db = _factory.CreateDbContext())
        {
            db.Series.Remove(await db.Series.FindAsync(seriesId) ?? throw new InvalidOperationException());
            await db.SaveChangesAsync();
        }

        var id = await vm.SaveAsync(seriesId);

        Assert.Null(id);
        Assert.True(vm.NotFound);
    }

    // --- Delete --------------------------------------------------------------

    [Fact]
    public async Task DeleteSeriesAsync_removesSeries_andSetNullsWorks()
    {
        var seriesId = await SeedSeriesAsync();
        var workId = await SeedWorkAsync("Mort", seriesId, order: 1);

        var vm = NewVm();
        var ok = await vm.DeleteSeriesAsync(seriesId);

        Assert.True(ok);
        await using var db = _factory.CreateDbContext();
        Assert.Null(await db.Series.FindAsync(seriesId));
        Assert.Null((await db.Works.FindAsync(workId))!.SeriesId); // detached, survives
    }

    // --- Work membership -----------------------------------------------------

    [Fact]
    public async Task AddWorkToSeriesAsync_attaches_andAppendsRowWithOrder()
    {
        var seriesId = await SeedSeriesAsync();
        await SeedWorkAsync("Colour of Magic", seriesId, order: 1);
        var newWorkId = await SeedWorkAsync("The Light Fantastic");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.AddWorkToSeriesAsync(seriesId, newWorkId);

        Assert.Equal(2, vm.Works.Count);
        var added = vm.Works.Single(w => w.Id == newWorkId);
        Assert.Equal(2, added.SeriesOrder);          // appended after order 1
        await using var db = _factory.CreateDbContext();
        Assert.Equal(seriesId, (await db.Works.FindAsync(newWorkId))!.SeriesId);
    }

    [Fact]
    public async Task RemoveWorkFromSeriesAsync_clearsWork_andDropsRow()
    {
        var seriesId = await SeedSeriesAsync();
        var workId = await SeedWorkAsync("Edgedancer", seriesId, order: 4, orderDisplay: "4.5");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.RemoveWorkFromSeriesAsync(workId);

        Assert.Empty(vm.Works);
        await using var db = _factory.CreateDbContext();
        var work = await db.Works.FindAsync(workId);
        Assert.Null(work!.SeriesId);
        Assert.Null(work.SeriesOrderDisplay); // dangling "4.5" cleared (the consistency fix)
    }

    [Fact]
    public async Task UpdateWorkOrderAsync_parsesPersists_andUpdatesRow()
    {
        var seriesId = await SeedSeriesAsync();
        var workId = await SeedWorkAsync("Words of Radiance", seriesId, order: 2);

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.UpdateWorkOrderAsync(workId, "4.5"); // interquel

        var row = vm.Works.Single(w => w.Id == workId);
        Assert.Equal(4, row.SeriesOrder);
        Assert.Equal("4.5", row.SeriesOrderDisplay);
        await using var db = _factory.CreateDbContext();
        var work = await db.Works.FindAsync(workId);
        Assert.Equal(4, work!.SeriesOrder);
        Assert.Equal("4.5", work.SeriesOrderDisplay);
    }

    [Fact]
    public async Task SaveAsync_failedSaveAfterSuccess_clearsStaleSuccessMessage()
    {
        var seriesId = await SeedSeriesAsync("Keeper");
        await SeedSeriesAsync("Taken");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        vm.Input!.Name = "Keeper Renamed";
        await vm.SaveAsync(seriesId);                          // success → SuccessMessage set
        Assert.False(string.IsNullOrEmpty(vm.SuccessMessage));

        vm.Input.Name = "Taken";                              // now collides with the other series
        var id = await vm.SaveAsync(seriesId);

        Assert.Null(id);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.True(string.IsNullOrEmpty(vm.SuccessMessage)); // no dual green+red banner
    }

    [Fact]
    public async Task AddWorkToSeriesAsync_calledTwiceForSameWork_addsOneRow()
    {
        var seriesId = await SeedSeriesAsync();
        var workId = await SeedWorkAsync("Sourcery");

        var vm = NewVm();
        await vm.InitializeAsync(seriesId);
        await vm.AddWorkToSeriesAsync(seriesId, workId);
        await vm.AddWorkToSeriesAsync(seriesId, workId);  // double-fire (e.g. double-click)

        Assert.Single(vm.Works);
        Assert.Equal(workId, vm.Works[0].Id);
    }
}
