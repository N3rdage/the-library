using BookTracker.Data.Models;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Tests.ViewModels;

public class CopyFormDialogViewModelTests
{
    [Fact]
    public void InitializeForAdd_SetsIsNewAndDefaults()
    {
        var factory = new TestDbContextFactory();
        var vm = new CopyFormDialogViewModel(factory);

        vm.InitializeForAdd(42);

        Assert.True(vm.IsNew);
        Assert.Equal(42, vm.EditionId);
        Assert.Null(vm.CopyId);
        Assert.NotNull(vm.DateAcquired); // today, by default
    }

    [Fact]
    public async Task InitializeForEditAsync_MissingId_MarksNotFound()
    {
        var factory = new TestDbContextFactory();
        var vm = new CopyFormDialogViewModel(factory);

        await vm.InitializeForEditAsync(999);

        Assert.True(vm.NotFound);
    }

    [Fact]
    public async Task InitializeForEditAsync_LoadsCopyFields()
    {
        var factory = new TestDbContextFactory();
        int copyId;
        using (var db = factory.CreateDbContext())
        {
            var copy = new Copy
            {
                Condition = BookCondition.VeryGood,
                DateAcquired = new DateTime(2024, 3, 15),
                Notes = "From a charity shop",
            };
            var edition = new Edition { Isbn = "x", Copies = [copy] };
            db.Books.Add(new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Editions = [edition],
            });
            await db.SaveChangesAsync();
            copyId = copy.Id;
        }

        var vm = new CopyFormDialogViewModel(factory);
        await vm.InitializeForEditAsync(copyId);

        Assert.False(vm.IsNew);
        Assert.False(vm.NotFound);
        Assert.Equal(BookCondition.VeryGood, vm.Condition);
        Assert.Equal(new DateTime(2024, 3, 15), vm.DateAcquired);
        Assert.Equal("From a charity shop", vm.Notes);
    }

    [Fact]
    public async Task SaveAsync_Add_AttachesCopyToEdition()
    {
        var factory = new TestDbContextFactory();
        int editionId;
        using (var db = factory.CreateDbContext())
        {
            var seedEdition = new Edition { Isbn = "x", Copies = [new Copy { Condition = BookCondition.Good }] };
            db.Books.Add(new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Editions = [seedEdition],
            });
            await db.SaveChangesAsync();
            editionId = seedEdition.Id;
        }

        var vm = new CopyFormDialogViewModel(factory);
        vm.InitializeForAdd(editionId);
        vm.Condition = BookCondition.Fine;
        vm.Notes = "  signed  ";
        var id = await vm.SaveAsync();

        Assert.NotNull(id);
        using var db2 = factory.CreateDbContext();
        var edition = db2.Editions.Include(e => e.Copies).Single(e => e.Id == editionId);
        Assert.Equal(2, edition.Copies.Count);
        var added = edition.Copies.Single(c => c.Id == id);
        Assert.Equal(BookCondition.Fine, added.Condition);
        Assert.Equal("signed", added.Notes);
    }

    [Fact]
    public async Task SaveAsync_Edit_UpdatesFields()
    {
        var factory = new TestDbContextFactory();
        int copyId;
        using (var db = factory.CreateDbContext())
        {
            var seedCopy = new Copy { Condition = BookCondition.Good, Notes = "old" };
            var edition = new Edition { Isbn = "x", Copies = [seedCopy] };
            db.Books.Add(new Book
            {
                Title = "B",
                Works = [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = new Author { Name = "A" }, Order = 0 }] }],
                Editions = [edition],
            });
            await db.SaveChangesAsync();
            copyId = seedCopy.Id;
        }

        var vm = new CopyFormDialogViewModel(factory);
        await vm.InitializeForEditAsync(copyId);
        vm.Condition = BookCondition.Fair;
        vm.Notes = "";
        await vm.SaveAsync();

        using var db2 = factory.CreateDbContext();
        var copy = db2.Copies.Single(c => c.Id == copyId);
        Assert.Equal(BookCondition.Fair, copy.Condition);
        Assert.Null(copy.Notes); // blank → null
    }
}
