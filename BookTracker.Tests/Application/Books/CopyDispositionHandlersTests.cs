using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookTracker.Tests;

// Integration tests for RemoveCopyFromCollection / RestoreCopyToCollection.
[Trait("Category", TestCategories.Integration)]
public class CopyDispositionHandlersTests
{
    private readonly TestDbContextFactory _factory = new();

    private static readonly DateOnly Removed = new(2026, 11, 20);

    // One book, one edition, one copy; returns the book id and copy id.
    private async Task<(int BookId, int CopyId)> SeedBookAsync()
    {
        await using var db = _factory.CreateDbContext();
        var copy = TestEditions.Copy();
        var book = TestBooks.Book("Dune", editions: [TestEditions.Edition(copies: [copy])]);
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return (book.Id, copy.Id);
    }

    private Task Remove(int copyId, CopyDisposition reason = CopyDisposition.Donated, string? note = null) =>
        new RemoveCopyFromCollectionHandler(_factory).HandleAsync(
            new RemoveCopyFromCollection(copyId, reason, Removed, note));

    private async Task<Copy> CopyAsync(int copyId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.Copies.SingleAsync(c => c.Id == copyId);
    }

    [Fact]
    public async Task RemoveCopyFromCollection_persistsDisposition()
    {
        var (_, copyId) = await SeedBookAsync();

        await Remove(copyId, CopyDisposition.Sold, "  eBay  ");

        var copy = await CopyAsync(copyId);
        Assert.Equal(CopyDisposition.Sold, copy.Disposition);
        Assert.Equal(Removed, copy.RemovedOn);
        Assert.Equal("eBay", copy.RemovalNote);
    }

    [Fact]
    public async Task RemoveCopyFromCollection_whileLent_throwsDomainRule_andStaysOwned()
    {
        var (_, copyId) = await SeedBookAsync();
        await using (var lending = _factory.CreateDbContext())
        {
            var lent = await lending.Copies.Include(c => c.Loans).SingleAsync(c => c.Id == copyId);
            lent.LendTo("Sam", Removed.AddDays(-5), null);
            await lending.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DomainRuleException>(() => Remove(copyId));

        var copy = await CopyAsync(copyId);
        Assert.Equal(CopyDisposition.Owned, copy.Disposition);
        Assert.Null(copy.RemovedOn);
    }

    [Fact]
    public async Task RemoveCopyFromCollection_missingCopy_throwsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Remove(424242));
    }

    [Fact]
    public async Task RemoveCopyFromCollection_bumpsBookUpdatedAt()
    {
        var (bookId, copyId) = await SeedBookAsync();
        DateTime before;
        await using (var seeded = _factory.CreateDbContext())
        {
            before = (await seeded.Books.FindAsync(bookId))!.UpdatedAt;
        }

        // Keeps the remove's save off the seed's clock tick (datetime2 is 100ns).
        await Task.Delay(TimeSpan.FromMilliseconds(20));

        await Remove(copyId);

        await using var db = _factory.CreateDbContext();
        var book = await db.Books.FindAsync(bookId);
        Assert.NotNull(book);
        Assert.True(book!.UpdatedAt > before,
            $"Expected UpdatedAt to bump. before={before:O}, after={book.UpdatedAt:O}");
    }

    [Fact]
    public async Task RestoreCopyToCollection_clearsRemoval()
    {
        var (_, copyId) = await SeedBookAsync();
        await Remove(copyId, CopyDisposition.Lost, "left on a train");

        await new RestoreCopyToCollectionHandler(_factory).HandleAsync(new RestoreCopyToCollection(copyId));

        var copy = await CopyAsync(copyId);
        Assert.Equal(CopyDisposition.Owned, copy.Disposition);
        Assert.Null(copy.RemovedOn);
        Assert.Null(copy.RemovalNote);
    }

    [Fact]
    public async Task GetBookDetail_projectsTheDisposition_andKeepsTheRemovedCopyVisible()
    {
        var (bookId, copyId) = await SeedBookAsync();
        await using (var db = _factory.CreateDbContext())
        {
            var edition = await db.Editions.SingleAsync(e => e.BookId == bookId);
            edition.AddCopy(BookCondition.Good, null, null);
            await db.SaveChangesAsync();
        }
        await Remove(copyId, CopyDisposition.Sold, "eBay");

        var detail = await new GetBookDetailHandler(_factory).HandleAsync(new GetBookDetail(bookId));

        var copies = Assert.Single(detail!.Editions).Copies.ToDictionary(c => c.Id);
        Assert.Equal(2, copies.Count);
        var removed = copies[copyId];
        Assert.Equal(CopyDisposition.Sold, removed.Disposition);
        Assert.Equal(Removed, removed.RemovedOn);
        Assert.Equal("eBay", removed.RemovalNote);
        Assert.False(removed.IsOwned);
        var owned = copies.Values.Single(c => c.Id != copyId);
        Assert.True(owned.IsOwned);
        Assert.Null(owned.RemovedOn);
    }

    [Fact]
    public async Task GetCopyForEdit_projectsTheDisposition()
    {
        var (_, copyId) = await SeedBookAsync();
        await Remove(copyId, CopyDisposition.Lost);

        var copy = await new GetCopyForEditHandler(_factory).HandleAsync(new GetCopyForEdit(copyId));

        Assert.Equal(CopyDisposition.Lost, copy!.Disposition);
        Assert.Equal(Removed, copy.RemovedOn);
    }

    [Fact]
    public async Task RestoreCopyToCollection_missingCopy_throwsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new RestoreCopyToCollectionHandler(_factory).HandleAsync(new RestoreCopyToCollection(424242)));
    }
}
