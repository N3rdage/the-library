using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// A Copy lent to someone: who has it (free text, not a user or contact
// record), when it went, when it is expected back, and when it came back.
// Part of the Book aggregate through Copy (Book → Edition → Copy → Loan), so a
// lend or a return bumps Book.UpdatedAt through the interceptor.
//
// A loan is open while ReturnedOn is null; a copy has at most one open loan
// (Copy.LendTo checks it, and a filtered unique index on CopyId enforces it in
// the database). Returned loans stay as history. Created only through
// Copy.LendTo and closed only through Copy.MarkReturned: the setters are
// private and EF materialises through them.
public class Loan
{
    public const int BorrowerNameMaxLength = 200;

    // EF materialisation.
    private Loan() { }

    internal Loan(string borrowerName, DateOnly lentOn, DateOnly? dueOn)
    {
        BorrowerName = borrowerName;
        LentOn = lentOn;
        DueOn = dueOn;
    }

    public int Id { get; private set; }

    public int CopyId { get; private set; }
    public Copy Copy { get; private set; } = null!;

    [Required, MaxLength(BorrowerNameMaxLength)]
    public string BorrowerName { get; private set; } = string.Empty;

    public DateOnly LentOn { get; private set; }

    public DateOnly? DueOn { get; private set; }

    public DateOnly? ReturnedOn { get; private set; }

    public bool IsOpen => ReturnedOn is null;

    internal void MarkReturned(DateOnly returnedOn) => ReturnedOn = returnedOn;
}
