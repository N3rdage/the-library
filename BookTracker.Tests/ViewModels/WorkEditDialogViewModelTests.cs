using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.ViewModels;

public class WorkEditDialogViewModelTests
{
    [Fact]
    public async Task InitializeAsync_MissingId_MarksNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new WorkEditDialogViewModel(factory);

        await vm.InitializeAsync(999);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeAsync_LoadsCurrentValues()
    {
        var factory = new TestDbContextFactory();
        int workId;
        using (var db = factory.CreateDbContext())
        {
            var series = new Series { Name = "Discworld", Type = SeriesType.Collection };
            var work = new Work
            {
                Title = "Mort",
                Subtitle = "A Discworld Novel",
                Author = new Author { Name = "Terry Pratchett" },
                FirstPublishedDate = new DateOnly(1987, 11, 12),
                FirstPublishedDatePrecision = DatePrecision.Day,
                Series = series,
                SeriesOrder = 4,
            };
            db.Books.Add(new Book { Title = "Mort", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);

        Assert.False(vm.NotFound);
        Assert.Equal("Mort", vm.Title);
        Assert.Equal("A Discworld Novel", vm.Subtitle);
        Assert.Equal("Terry Pratchett", vm.AuthorName);
        Assert.Equal("12 Nov 1987", vm.FirstPublishedDate);
        Assert.NotNull(vm.SelectedSeriesId);
        Assert.Equal(4, vm.SeriesOrder);
        Assert.Single(vm.AvailableSeries);
    }

    [Fact]
    public async Task SaveAsync_PersistsBasicFields()
    {
        var factory = new TestDbContextFactory();
        int workId;
        using (var db = factory.CreateDbContext())
        {
            var work = new Work
            {
                Title = "Old",
                Author = new Author { Name = "Old Author" },
            };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);
        vm.Title = "  Mort  ";
        vm.Subtitle = "A Discworld Novel";
        vm.AuthorName = "Old Author";
        vm.FirstPublishedDate = "1987";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var saved = db2.Works.Include(w => w.Author).Single(w => w.Id == workId);
        Assert.Equal("Mort", saved.Title);
        Assert.Equal("A Discworld Novel", saved.Subtitle);
        Assert.Equal(new DateOnly(1987, 1, 1), saved.FirstPublishedDate);
        Assert.Equal(DatePrecision.Year, saved.FirstPublishedDatePrecision);
    }

    [Fact]
    public async Task SaveAsync_ReusesExistingAuthorByName()
    {
        var factory = new TestDbContextFactory();
        int workId;
        int existingAuthorId;
        using (var db = factory.CreateDbContext())
        {
            var a1 = new Author { Name = "Terry Pratchett" };
            var a2 = new Author { Name = "Placeholder" };
            var work = new Work { Title = "w", Author = a2 };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            db.Authors.Add(a1);
            await db.SaveChangesAsync();
            workId = work.Id;
            existingAuthorId = a1.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);
        vm.AuthorName = "Terry Pratchett";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var saved = db2.Works.Include(w => w.Author).Single(w => w.Id == workId);
        Assert.Equal(existingAuthorId, saved.Author.Id);
        Assert.Equal(2, db2.Authors.Count());
    }

    [Fact]
    public async Task SaveAsync_CreatesNewAuthorWhenNameIsNew()
    {
        var factory = new TestDbContextFactory();
        int workId;
        using (var db = factory.CreateDbContext())
        {
            var work = new Work { Title = "w", Author = new Author { Name = "Old Author" } };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);
        vm.AuthorName = "Brand New Author";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        Assert.Contains(db2.Authors.ToList(), a => a.Name == "Brand New Author");
    }

    [Fact]
    public async Task SaveAsync_ClearingSeriesAlsoClearsOrder()
    {
        var factory = new TestDbContextFactory();
        int workId;
        using (var db = factory.CreateDbContext())
        {
            var series = new Series { Name = "S", Type = SeriesType.Series };
            var work = new Work
            {
                Title = "w",
                Author = new Author { Name = "a" },
                Series = series,
                SeriesOrder = 3,
            };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);
        vm.SelectedSeriesId = null;
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var saved = db2.Works.Single(w => w.Id == workId);
        Assert.Null(saved.SeriesId);
        Assert.Null(saved.SeriesOrder);
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingGenreIds()
    {
        var factory = new TestDbContextFactory();
        int workId;
        int fantasyId;
        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var work = new Work
            {
                Title = "w",
                Author = new Author { Name = "a" },
                Genres = [fantasy],
            };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
            fantasyId = fantasy.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);

        Assert.Single(vm.SelectedGenreIds);
        Assert.Contains(fantasyId, vm.SelectedGenreIds);
    }

    [Fact]
    public async Task SaveAsync_ReconcilesGenreSelection()
    {
        var factory = new TestDbContextFactory();
        int workId;
        int fantasyId;
        int horrorId;
        using (var db = factory.CreateDbContext())
        {
            var fantasy = new Genre { Name = "Fantasy" };
            var horror = new Genre { Name = "Horror" };
            db.Genres.AddRange(fantasy, horror);
            var work = new Work
            {
                Title = "w",
                Author = new Author { Name = "a" },
                Genres = [fantasy], // starts with Fantasy
            };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
            fantasyId = fantasy.Id;
            horrorId = horror.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);
        // Swap selection: drop Fantasy, add Horror.
        vm.SelectedGenreIds = [horrorId];
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var saved = db2.Works.Include(w => w.Genres).Single(w => w.Id == workId);
        Assert.Single(saved.Genres);
        Assert.Equal(horrorId, saved.Genres[0].Id);
    }

    [Fact]
    public async Task SearchAuthorsAsync_MatchesSubstring()
    {
        var factory = new TestDbContextFactory();
        int workId;
        using (var db = factory.CreateDbContext())
        {
            db.Authors.AddRange(
                new Author { Name = "Terry Pratchett" },
                new Author { Name = "Neil Gaiman" },
                new Author { Name = "Pratchett & Gaiman" });
            var work = new Work { Title = "w", Author = new Author { Name = "Seed" } };
            db.Books.Add(new Book { Title = "B", Works = [work] });
            await db.SaveChangesAsync();
            workId = work.Id;
        }

        var vm = new WorkEditDialogViewModel(factory);
        await vm.InitializeAsync(workId);
        var results = (await vm.SearchAuthorsAsync("Pratch", CancellationToken.None)).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains("Terry Pratchett", results);
        Assert.Contains("Pratchett & Gaiman", results);
    }
}
