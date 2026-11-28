using BookTracker.Data;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Copy.RemoveFromCollection / Copy.RestoreToCollection invariants, no EF.
[Trait("Category", TestCategories.Unit)]
public class CopyDispositionTests
{
    private static readonly DateOnly Removed = new(2026, 11, 20);

    [Fact]
    public void NewCopy_isOwned()
    {
        var copy = TestEditions.Copy();

        Assert.Equal(CopyDisposition.Owned, copy.Disposition);
        Assert.True(copy.IsOwned);
        Assert.Null(copy.RemovedOn);
        Assert.Null(copy.RemovalNote);
    }

    [Fact]
    public void RemoveFromCollection_setsFields_andTrimsNote()
    {
        var copy = TestEditions.Copy();

        copy.RemoveFromCollection(CopyDisposition.Donated, Removed, "  Vinnies  ");

        Assert.Equal(CopyDisposition.Donated, copy.Disposition);
        Assert.False(copy.IsOwned);
        Assert.Equal(Removed, copy.RemovedOn);
        Assert.Equal("Vinnies", copy.RemovalNote);
    }

    [Fact]
    public void RemoveFromCollection_blankNote_storesNull()
    {
        var copy = TestEditions.Copy();

        copy.RemoveFromCollection(CopyDisposition.Lost, Removed, "   ");

        Assert.Null(copy.RemovalNote);
    }

    [Theory]
    [InlineData(CopyDisposition.Owned)]
    [InlineData((CopyDisposition)99)]
    public void RemoveFromCollection_ownedOrUndefinedReason_throws(CopyDisposition reason)
    {
        var copy = TestEditions.Copy();

        Assert.Throws<DomainRuleException>(() => copy.RemoveFromCollection(reason, Removed, null));
        Assert.True(copy.IsOwned);
        Assert.Null(copy.RemovedOn);
    }

    [Fact]
    public void RemoveFromCollection_twice_throws_andKeepsFirstRemoval()
    {
        var copy = TestEditions.Copy();
        copy.RemoveFromCollection(CopyDisposition.Sold, Removed, "eBay");

        Assert.Throws<DomainRuleException>(() =>
            copy.RemoveFromCollection(CopyDisposition.Lost, Removed.AddDays(3), "gone"));

        Assert.Equal(CopyDisposition.Sold, copy.Disposition);
        Assert.Equal(Removed, copy.RemovedOn);
        Assert.Equal("eBay", copy.RemovalNote);
    }

    [Fact]
    public void RemoveFromCollection_whileLent_throws_andStaysOwned()
    {
        var copy = TestEditions.Copy();
        copy.LendTo("Sam", Removed.AddDays(-5), null);

        var ex = Assert.Throws<DomainRuleException>(() =>
            copy.RemoveFromCollection(CopyDisposition.Lost, Removed, null));

        Assert.Equal("This copy is lent out. Return the loan before removing it from the collection.", ex.Message);
        Assert.True(copy.IsOwned);
    }

    [Fact]
    public void RemoveFromCollection_afterLoanReturned_isAllowed_andKeepsHistory()
    {
        var copy = TestEditions.Copy();
        var loan = copy.LendTo("Sam", Removed.AddDays(-5), null);
        copy.MarkReturned(loan.Id, Removed.AddDays(-1));

        copy.RemoveFromCollection(CopyDisposition.Damaged, Removed, null);

        Assert.Equal(CopyDisposition.Damaged, copy.Disposition);
        Assert.Single(copy.Loans);
    }

    [Fact]
    public void RemoveFromCollection_noteOverMaxLength_throws()
    {
        var copy = TestEditions.Copy();

        Assert.Throws<DomainRuleException>(() => copy.RemoveFromCollection(
            CopyDisposition.Donated, Removed, new string('a', Copy.RemovalNoteMaxLength + 1)));
        Assert.True(copy.IsOwned);
    }

    [Fact]
    public void RemoveFromCollection_noteAtMaxLengthAfterTrim_isAllowed()
    {
        var copy = TestEditions.Copy();
        var note = new string('a', Copy.RemovalNoteMaxLength);

        copy.RemoveFromCollection(CopyDisposition.Donated, Removed, $"  {note}  ");

        Assert.Equal(note, copy.RemovalNote);
    }

    [Fact]
    public void RestoreToCollection_clearsRemoval()
    {
        var copy = TestEditions.Copy();
        copy.RemoveFromCollection(CopyDisposition.Lost, Removed, "left on a train");

        copy.RestoreToCollection();

        Assert.Equal(CopyDisposition.Owned, copy.Disposition);
        Assert.True(copy.IsOwned);
        Assert.Null(copy.RemovedOn);
        Assert.Null(copy.RemovalNote);
    }

    [Fact]
    public void RestoreToCollection_ownedCopy_throws()
    {
        var copy = TestEditions.Copy();

        Assert.Throws<DomainRuleException>(() => copy.RestoreToCollection());
    }

    [Fact]
    public void LendTo_removedCopy_throws_andOpensNoLoan()
    {
        var copy = TestEditions.Copy();
        copy.RemoveFromCollection(CopyDisposition.Donated, Removed, null);

        Assert.Throws<DomainRuleException>(() => copy.LendTo("Sam", Removed.AddDays(1), null));
        Assert.Empty(copy.Loans);
    }

    [Fact]
    public void LendTo_afterRestore_isAllowed()
    {
        var copy = TestEditions.Copy();
        copy.RemoveFromCollection(CopyDisposition.Lost, Removed, null);
        copy.RestoreToCollection();

        var loan = copy.LendTo("Sam", Removed.AddDays(1), null);

        Assert.Same(loan, copy.OpenLoan);
    }
}
