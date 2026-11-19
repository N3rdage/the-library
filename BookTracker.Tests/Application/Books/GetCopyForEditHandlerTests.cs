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
                works: [new Work { Title = "B", WorkAuthors = [new WorkAuthor { Author = Author.Create("A"), Order = 0 }] }],
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
    }
}
