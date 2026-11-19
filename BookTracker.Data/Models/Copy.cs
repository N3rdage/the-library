namespace BookTracker.Data.Models;

public class Copy
{
    public int Id { get; set; }

    public int EditionId { get; set; }
    public Edition Edition { get; set; } = null!;

    public BookCondition Condition { get; set; } = BookCondition.Good;

    public DateTime? DateAcquired { get; set; }

    public string? Notes { get; set; }

    /// <summary>Every loan of this copy, open and returned. Written only through
    /// <see cref="LendTo"/> and <see cref="MarkReturned"/>; callers that lend or
    /// return must load it so the open-loan check sees the real set.</summary>
    public List<Loan> Loans { get; private set; } = [];

    /// <summary>The loan not yet returned, or null when the copy is on the
    /// shelf. Computed from <see cref="Loans"/>; not mapped.</summary>
    public Loan? OpenLoan => Loans.FirstOrDefault(l => l.IsOpen);

    public void UpdateDetails(BookCondition condition, DateTime? dateAcquired, string? notes)
    {
        Condition = condition;
        DateAcquired = dateAcquired;
        Notes = notes.TrimToNull();
    }

    /// <summary>Lends this copy to <paramref name="borrowerName"/> (trimmed, at
    /// most 200 characters) from <paramref name="lentOn"/>, optionally due back
    /// on <paramref name="dueOn"/>. Throws when the copy is already lent, the
    /// name is blank or too long, or the due date is before the lend date.
    /// Returns the new loan.</summary>
    public Loan LendTo(string borrowerName, DateOnly lentOn, DateOnly? dueOn)
    {
        if (OpenLoan is not null)
            throw new DomainRuleException("This copy is already lent out. Return the loan first.");
        if (string.IsNullOrWhiteSpace(borrowerName))
            throw new DomainRuleException("Borrower name is required.");
        var name = borrowerName.Trim();
        if (name.Length > Loan.BorrowerNameMaxLength)
            throw new DomainRuleException($"Borrower name must be at most {Loan.BorrowerNameMaxLength} characters.");
        if (dueOn < lentOn)
            throw new DomainRuleException("The due date cannot be before the lend date.");

        var loan = new Loan(name, lentOn, dueOn);
        Loans.Add(loan);
        return loan;
    }

    /// <summary>Closes the open loan on <paramref name="returnedOn"/>. Throws when
    /// the copy has no open loan or the return date is before the lend date.
    /// Returns the closed loan.</summary>
    public Loan MarkReturned(DateOnly returnedOn)
    {
        var loan = OpenLoan
            ?? throw new DomainRuleException("This copy is not lent out.");
        if (returnedOn < loan.LentOn)
            throw new DomainRuleException("The return date cannot be before the lend date.");
        loan.MarkReturned(returnedOn);
        return loan;
    }
}
