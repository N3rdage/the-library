using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// The physical item you own; part of the Book aggregate through its Edition.
// The scalar fields are written only through the methods below (private
// setters; EF materialises through them), and the compiler enforces it. A Copy
// is created through Edition.AddCopy; the internal constructor serves that and
// the tests' seed builder (InternalsVisibleTo). Id stays public for EF and the
// tests' in-memory graphs. A copy that has left the collection (donated,
// lost, damaged, sold) stays as a row with its Disposition, removal date and
// note, so its loan history survives; it cannot be lent while removed.
public class Copy
{
    public const int RemovalNoteMaxLength = 500;

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

    public CopyDisposition Disposition { get; private set; } = CopyDisposition.Owned;

    /// <summary>The day the copy left the collection; null while owned.</summary>
    public DateOnly? RemovedOn { get; private set; }

    [MaxLength(RemovalNoteMaxLength)]
    public string? RemovalNote { get; private set; }

    /// <summary>True while the copy is in the collection. Computed from
    /// <see cref="Disposition"/>; not mapped.</summary>
    public bool IsOwned => Disposition == CopyDisposition.Owned;

    /// <summary>Every loan of this copy, open and returned. Written only through
    /// <see cref="LendTo"/> and <see cref="MarkReturned"/>; callers that lend or
    /// return must load it so the open-loan check sees the real set.</summary>
    public IReadOnlyCollection<Loan> Loans => _loans;
    private readonly List<Loan> _loans = [];

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
    /// on <paramref name="dueOn"/>. Throws when the copy has been removed from
    /// the collection or is already lent, the name is blank or too long, or the
    /// due date is before the lend date. Returns the new loan.</summary>
    public Loan LendTo(string borrowerName, DateOnly lentOn, DateOnly? dueOn)
    {
        if (!IsOwned)
            throw new DomainRuleException("This copy has been removed from the collection. Restore it before lending it.");
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
        _loans.Add(loan);
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

    /// <summary>Marks this copy as gone from the collection for
    /// <paramref name="reason"/> on <paramref name="removedOn"/>, with an
    /// optional note (trimmed, at most <see cref="RemovalNoteMaxLength"/>
    /// characters). Throws when the reason is Owned or not a defined value,
    /// the copy is already removed, the note is too long, or the copy is lent
    /// out.</summary>
    public void RemoveFromCollection(CopyDisposition reason, DateOnly removedOn, string? note)
    {
        if (reason == CopyDisposition.Owned || !Enum.IsDefined(reason))
            throw new DomainRuleException("Choose why the copy is leaving the collection.");
        if (!IsOwned)
            throw new DomainRuleException("This copy has already been removed from the collection.");
        var trimmed = note.TrimToNull();
        if (trimmed?.Length > RemovalNoteMaxLength)
            throw new DomainRuleException($"The removal note must be at most {RemovalNoteMaxLength} characters.");
        if (OpenLoan is not null)
            throw new DomainRuleException("This copy is lent out. Return the loan before removing it from the collection.");

        Disposition = reason;
        RemovedOn = removedOn;
        RemovalNote = trimmed;
    }

    /// <summary>Brings a removed copy back into the collection, clearing the
    /// removal date and note. Throws when the copy is already owned.</summary>
    public void RestoreToCollection()
    {
        if (IsOwned)
            throw new DomainRuleException("This copy is already in the collection.");

        Disposition = CopyDisposition.Owned;
        RemovedOn = null;
        RemovalNote = null;
    }
}
