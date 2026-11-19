using BookTracker.Data;
using BookTracker.Data.Models;
using Xunit;

namespace BookTracker.Tests;

// Copy.LendTo / Copy.MarkReturned invariants, no EF.
[Trait("Category", TestCategories.Unit)]
public class CopyLoanTests
{
    private static readonly DateOnly Lent = new(2026, 11, 1);

    [Fact]
    public void LendTo_opensLoan_withTrimmedName()
    {
        var copy = TestEditions.Copy();

        var loan = copy.LendTo("  Sam  ", Lent, Lent.AddDays(14));

        Assert.Same(loan, copy.OpenLoan);
        Assert.Single(copy.Loans);
        Assert.Equal("Sam", loan.BorrowerName);
        Assert.Equal(Lent, loan.LentOn);
        Assert.Equal(Lent.AddDays(14), loan.DueOn);
        Assert.Null(loan.ReturnedOn);
        Assert.True(loan.IsOpen);
    }

    [Fact]
    public void LendTo_withoutDueDate_isAllowed()
    {
        var copy = TestEditions.Copy();
        var loan = copy.LendTo("Sam", Lent, null);
        Assert.Null(loan.DueOn);
    }

    [Fact]
    public void LendTo_dueSameDayAsLent_isAllowed()
    {
        var copy = TestEditions.Copy();
        Assert.Equal(Lent, copy.LendTo("Sam", Lent, Lent).DueOn);
    }

    [Fact]
    public void LendTo_whenAlreadyLent_throws_andKeepsTheOpenLoan()
    {
        var copy = TestEditions.Copy();
        var first = copy.LendTo("Sam", Lent, null);

        Assert.Throws<DomainRuleException>(() => copy.LendTo("Alex", Lent, null));
        Assert.Single(copy.Loans);
        Assert.Same(first, copy.OpenLoan);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LendTo_blankName_throws(string name)
    {
        var copy = TestEditions.Copy();
        Assert.Throws<DomainRuleException>(() => copy.LendTo(name, Lent, null));
        Assert.Empty(copy.Loans);
    }

    [Fact]
    public void LendTo_nameOverMaxLength_throws()
    {
        var copy = TestEditions.Copy();
        Assert.Throws<DomainRuleException>(() =>
            copy.LendTo(new string('a', Loan.BorrowerNameMaxLength + 1), Lent, null));
    }

    [Fact]
    public void LendTo_nameAtMaxLengthAfterTrim_isAllowed()
    {
        var copy = TestEditions.Copy();
        var name = new string('a', Loan.BorrowerNameMaxLength);
        Assert.Equal(name, copy.LendTo($"  {name}  ", Lent, null).BorrowerName);
    }

    [Fact]
    public void LendTo_dueBeforeLent_throws()
    {
        var copy = TestEditions.Copy();
        Assert.Throws<DomainRuleException>(() => copy.LendTo("Sam", Lent, Lent.AddDays(-1)));
        Assert.Empty(copy.Loans);
    }

    [Fact]
    public void MarkReturned_closesOpenLoan_andKeepsItAsHistory()
    {
        var copy = TestEditions.Copy();
        var loan = copy.LendTo("Sam", Lent, null);

        var returned = copy.MarkReturned(Lent.AddDays(3));

        Assert.Same(loan, returned);
        Assert.Equal(Lent.AddDays(3), loan.ReturnedOn);
        Assert.False(loan.IsOpen);
        Assert.Null(copy.OpenLoan);
        Assert.Single(copy.Loans);
    }

    [Fact]
    public void MarkReturned_sameDayAsLent_isAllowed()
    {
        var copy = TestEditions.Copy();
        copy.LendTo("Sam", Lent, null);
        Assert.Equal(Lent, copy.MarkReturned(Lent).ReturnedOn);
    }

    [Fact]
    public void MarkReturned_withoutOpenLoan_throws()
    {
        var copy = TestEditions.Copy();
        Assert.Throws<DomainRuleException>(() => copy.MarkReturned(Lent));
    }

    [Fact]
    public void MarkReturned_beforeLent_throws_andLeavesLoanOpen()
    {
        var copy = TestEditions.Copy();
        var loan = copy.LendTo("Sam", Lent, null);

        Assert.Throws<DomainRuleException>(() => copy.MarkReturned(Lent.AddDays(-1)));
        Assert.True(loan.IsOpen);
    }

    [Fact]
    public void LendTo_afterReturn_opensSecondLoan()
    {
        var copy = TestEditions.Copy();
        copy.LendTo("Sam", Lent, null);
        copy.MarkReturned(Lent.AddDays(2));

        var second = copy.LendTo("Alex", Lent.AddDays(5), null);

        Assert.Equal(2, copy.Loans.Count);
        Assert.Same(second, copy.OpenLoan);
    }
}
