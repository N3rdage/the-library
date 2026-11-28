using BookTracker.Application.Books;
using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Integration tests for the Edit Copy dialog's prefill read (moved out of
// CopyFormDialogViewModel, #264).
[Trait("Category", TestCategories.Integration)]
public class GetCopyForEditHandlerTests
{
    private readonly TestDbContextFactory _factory = new();

    private Task<CopyForEdit?> Load(int copyId) =>
        new GetCopyForEditHandler(_factory).HandleAsync(new GetCopyForEdit(copyId));

    [Fact]
    public async Task MissingId_ReturnsNull()
    {
        Assert.Null(await Load(999));
    }

    [Fact]
    public async Task LoadsCopyFieldsAndEditionId()
    {
        int copyId, editionId;
        using (var db = _factory.CreateDbContext())
        {
            var copy = TestEditions.Copy(
                condition: BookCondition.VeryGood,
                dateAcquired: new DateTime(2024, 3, 15),
                notes: "From a charity shop");
            var edition = TestEditions.Edition(isbn: "x", copies: [copy]);
            db.Books.Add(TestBooks.Book(
                "B",
                works: [TestWorks.Work("B", Author.Create("A"))],
                editions: [edition]));
            await db.SaveChangesAsync();
            copyId = copy.Id;
            editionId = edition.Id;
        }

        var copyForEdit = await Load(copyId);

        Assert.NotNull(copyForEdit);
        Assert.Equal(copyId, copyForEdit.Id);
        Assert.Equal(editionId, copyForEdit.EditionId);
        Assert.Equal(BookCondition.VeryGood, copyForEdit.Condition);
        Assert.Equal(new DateTime(2024, 3, 15), copyForEdit.DateAcquired);
        Assert.Equal("From a charity shop", copyForEdit.Notes);
        Assert.False(copyForEdit.IsLent);
    }

    [Fact]
    public async Task IsLent_trueOnlyWhileALoanIsOpen()
    {
        var lent = TestEditions.Copy();
        lent.LendTo("Sam", new DateOnly(2026, 11, 1), null);
        var returned = TestEditions.Copy();
        returned.LendTo("Alex", new DateOnly(2026, 10, 1), null);
        returned.MarkReturned(returned.OpenLoan!.Id, new DateOnly(2026, 10, 5));
        int lentId, returnedId;
        using (var db = _factory.CreateDbContext())
        {
            db.Books.Add(TestBooks.Book(
                "B",
                works: [TestWorks.Work("B", Author.Create("A"))],
                editions: [TestEditions.Edition(isbn: "y", copies: [lent, returned])]));
            await db.SaveChangesAsync();
            lentId = lent.Id;
            returnedId = returned.Id;
        }

        Assert.True((await Load(lentId))!.IsLent);
        Assert.False((await Load(returnedId))!.IsLent);
    }
}
