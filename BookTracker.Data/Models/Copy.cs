namespace BookTracker.Data.Models;

// The physical item you own; part of the Book aggregate through its Edition.
// The scalar fields are written only through the methods below (private
// setters; EF materialises through them), and the compiler enforces it. A Copy
// is created through Edition.AddCopy; the internal constructor serves that and
// the tests' seed builder (InternalsVisibleTo). Id stays public for EF and the
// tests' in-memory graphs.
public class Copy
{
    // EF materialisation.
    private Copy() { }

    /// <summary>Stores exactly the values given (<see cref="Edition.AddCopy"/>
    /// trims the notes before calling).</summary>
    internal Copy(BookCondition condition, DateTime? dateAcquired, string? notes)
    {
        Condition = condition;
        DateAcquired = dateAcquired;
        Notes = notes;
    }

    public int Id { get; set; }

    public int EditionId { get; private set; }

    /// <summary>The owning Edition. EF fixes it up from <see cref="Edition.Copies"/>
    /// and <see cref="EditionId"/>; the setter is internal so the tests can attach
    /// a detached seed to its Edition (InternalsVisibleTo).</summary>
    public Edition Edition { get; internal set; } = null!;

    public BookCondition Condition { get; private set; } = BookCondition.Good;

    public DateTime? DateAcquired { get; private set; }

    public string? Notes { get; private set; }

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

    /// <summary>Re-points this Copy at Edition <paramref name="editionId"/> by key.
    /// Internal: <see cref="Edition.AdoptCopiesFrom"/> is the write path, and the
    /// tests' seed builder reaches it (InternalsVisibleTo).</summary>
    internal void MoveToEdition(int editionId) => EditionId = editionId;

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

    /// <summary>Closes loan <paramref name="loanId"/> on <paramref name="returnedOn"/>.
    /// Throws when that loan is not this copy's open loan (already returned,
    /// another copy's, or the copy is not lent out), or when the return date is
    /// before the lend date. Returns the closed loan.</summary>
    public Loan MarkReturned(int loanId, DateOnly returnedOn)
    {
        var loan = OpenLoan;
        if (loan is null || loan.Id != loanId)
            throw new DomainRuleException("This loan has already been returned.");
        if (returnedOn < loan.LentOn)
            throw new DomainRuleException("The return date cannot be before the lend date.");
        loan.MarkReturned(returnedOn);
        return loan;
    }
}
