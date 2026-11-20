using BookTracker.Application;
using BookTracker.Application.Formatting;
using BookTracker.Application.Loans;
using BookTracker.Data;

namespace BookTracker.Web.ViewModels;

// View model for /loans: the copies currently lent out, grouped by borrower in
// the order GetOpenLoans returns them (borrower, then due date), plus the
// returned-loan history loaded on first expand. "Today" is the owner's calendar
// date from LoanDates, so the overdue flag and the return date agree with the
// rest of the lending surface. A return removes the row in place; a refused
// return (already returned, loan gone) shows the message and reloads the list.
public class LoansViewModel(IDispatcher dispatcher, TimeProvider clock)
{
    public List<BorrowerLoans> Groups { get; private set; } = [];
    public bool Loaded { get; private set; }
    public DateOnly Today { get; private set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Loans with a return in flight, so the page can disable their
    /// buttons against a double click.</summary>
    public HashSet<int> ReturningLoanIds { get; } = [];

    public List<LoanHistoryRow> History { get; private set; } = [];
    public bool HistoryLoaded { get; private set; }

    public async Task LoadAsync()
    {
        Today = LoanDates.Today(clock);
        var rows = await dispatcher.Query(new GetOpenLoans());
        Groups = rows
            .GroupBy(r => r.BorrowerName, StringComparer.Ordinal)
            .Select(g => new BorrowerLoans(g.Key, g.ToList()))
            .ToList();
        Loaded = true;
    }

    public bool IsOverdue(OpenLoanRow loan) => LoanDates.IsOverdue(loan.DueOn, Today);

    public async Task ReturnAsync(int loanId)
    {
        if (!ReturningLoanIds.Add(loanId)) return;
        ErrorMessage = null;
        try
        {
            await dispatcher.Send(new ReturnLoan(loanId, LoanDates.Today(clock)));
            foreach (var group in Groups)
                group.Loans.RemoveAll(l => l.LoanId == loanId);
            Groups.RemoveAll(g => g.Loans.Count == 0);
            if (HistoryLoaded) await ReloadHistoryAsync();
        }
        catch (DomainRuleException ex)
        {
            ErrorMessage = ex.Message;
            await LoadAsync();
        }
        catch (NotFoundException)
        {
            ErrorMessage = "That loan no longer exists.";
            await LoadAsync();
        }
        finally
        {
            ReturningLoanIds.Remove(loanId);
        }
    }

    /// <summary>Loads the returned-loan history the first time it is asked for;
    /// later calls keep what is loaded.</summary>
    public async Task EnsureHistoryLoadedAsync()
    {
        if (HistoryLoaded) return;
        await ReloadHistoryAsync();
    }

    private async Task ReloadHistoryAsync()
    {
        History = (await dispatcher.Query(new GetLoanHistory())).ToList();
        HistoryLoaded = true;
    }

    public record BorrowerLoans(string BorrowerName, List<OpenLoanRow> Loans);
}
