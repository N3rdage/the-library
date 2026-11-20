using BookTracker.Application;
using BookTracker.Application.Formatting;
using BookTracker.Application.Loans;
using BookTracker.Data;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

// View model for LendDialog. The page that opens the dialog creates one,
// seeds it with the copy whose "Lend" was pressed, or with none (Initialize), and passes it
// in as a parameter. The user names the borrower, sets the dates, adds or
// removes copies, and ConfirmAsync lends every chosen copy in one LendCopies.
// A rule the handler refuses (already lent, blank name, due before lent, a
// copy gone) comes back as ErrorMessage and the dialog stays open.
public class LendDialogViewModel(IDispatcher dispatcher, TimeProvider clock)
{
    /// <summary>A copy on the lend list, or a candidate offered by the
    /// "Add another book" search.</summary>
    public record LendCopyOption(int CopyId, string Title, string EditionLabel, BookCondition Condition)
    {
        public string Display => $"{Title} — {EditionLabel} ({ConditionLabel(Condition)})";
    }

    public string? BorrowerName { get; set; }

    // DateTime? because that is what MudDatePicker binds; only the date part is used.
    public DateTime? LentOn { get; set; }
    public DateTime? DueOn { get; set; }

    public List<LendCopyOption> Copies { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public bool Saving { get; private set; }

    public bool CanConfirm =>
        !Saving && Copies.Count > 0 && LentOn is not null && !string.IsNullOrWhiteSpace(BorrowerName);

    public void Initialize(LendCopyOption first)
    {
        ArgumentNullException.ThrowIfNull(first);
        Reset([first]);
    }

    /// <summary>Starts with no copy chosen, for the /loans "Lend a book" entry
    /// point: the user finds the books with the dialog's search.</summary>
    public void Initialize() => Reset([]);

    private void Reset(List<LendCopyOption> copies)
    {
        Copies = copies;
        BorrowerName = null;
        LentOn = LoanDates.Today(clock).ToDateTime(TimeOnly.MinValue);
        DueOn = null;
        ErrorMessage = null;
        Saving = false;
    }

    /// <summary>The success snackbar text after <see cref="ConfirmAsync"/> returned true.</summary>
    public string LentMessage =>
        Copies.Count == 1 ? $"Lent to {BorrowerName?.Trim()}" : $"{Copies.Count} copies lent to {BorrowerName?.Trim()}";

    public async Task<IEnumerable<string>> SuggestBorrowersAsync(string? prefix, CancellationToken ct) =>
        await dispatcher.Query(new GetBorrowerNames(prefix), ct);

    /// <summary>Free copies of books matching <paramref name="query"/>, one
    /// option per copy, leaving out copies already on the list.</summary>
    public async Task<IEnumerable<LendCopyOption>> SearchCopiesAsync(string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var books = await dispatcher.Query(new SearchLendableBooks(query), ct);
        var chosen = Copies.Select(c => c.CopyId).ToHashSet();
        return books
            .SelectMany(b => b.Copies.Select(c => new LendCopyOption(c.CopyId, b.Title, c.EditionLabel, c.Condition)))
            .Where(o => !chosen.Contains(o.CopyId))
            .ToList();
    }

    public void AddCopy(LendCopyOption? option)
    {
        if (option is null || Copies.Any(c => c.CopyId == option.CopyId)) return;
        Copies.Add(option);
        ErrorMessage = null;
    }

    public void RemoveCopy(int copyId) => Copies.RemoveAll(c => c.CopyId == copyId);

    /// <summary>Lends every listed copy in one command. True when the loans were
    /// saved; false (with <see cref="ErrorMessage"/> set when the handler refused)
    /// otherwise.</summary>
    public async Task<bool> ConfirmAsync(CancellationToken ct = default)
    {
        if (!CanConfirm) return false;

        Saving = true;
        ErrorMessage = null;
        try
        {
            await dispatcher.Send(new LendCopies(
                Copies.Select(c => c.CopyId).ToList(),
                BorrowerName!.Trim(),
                DateOnly.FromDateTime(LentOn!.Value),
                DueOn is { } due ? DateOnly.FromDateTime(due) : null), ct);
            return true;
        }
        catch (Exception ex) when (ex is DomainRuleException or NotFoundException)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            Saving = false;
        }
    }

    public static string ConditionLabel(BookCondition c) => c switch
    {
        BookCondition.AsNew => "As New",
        BookCondition.VeryGood => "Very Good",
        _ => c.ToString(),
    };
}
