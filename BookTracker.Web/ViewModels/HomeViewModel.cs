using BookTracker.Application;
using BookTracker.Application.Formatting;
using BookTracker.Application.Home;
using BookTracker.Application.Loans;

namespace BookTracker.Web.ViewModels;

// View model for Home: the dashboard counts and top-10 lists, plus the "Due
// back" panel (GetDueBack). "Today" is the owner's calendar date from
// LoanDates, so the overdue flag agrees with /loans.
public class HomeViewModel(IDispatcher dispatcher, TimeProvider clock) : DispatchingViewModel
{
    public int TotalBooks { get; private set; }
    public int TotalAuthors { get; private set; }
    public IReadOnlyList<AuthorCount> TopAuthors { get; private set; } = [];
    public IReadOnlyList<GenreCount> TopGenres { get; private set; } = [];
    public int MaxAuthor { get; private set; }
    public int MaxGenre { get; private set; }

    public IReadOnlyList<DueBackRow> DueBack { get; private set; } = [];
    public int OpenEndedLoanCount { get; private set; }
    public DateOnly Today { get; private set; }

    /// <summary>The panel shows only when something is out on loan.</summary>
    public bool ShowDueBack => DueBack.Count > 0 || OpenEndedLoanCount > 0;

    public async Task InitializeAsync()
    {
        Today = LoanDates.Today(clock);
        var dashboard = await dispatcher.Query(new GetHomeDashboard());
        var dueBack = await dispatcher.Query(new GetDueBack());

        TotalBooks = dashboard.TotalBooks;
        TotalAuthors = dashboard.TotalAuthors;
        TopAuthors = dashboard.TopAuthors;
        TopGenres = dashboard.TopGenres;

        // Bar-scaling maxima are presentation-derived from the lists.
        MaxAuthor = TopAuthors.Count > 0 ? TopAuthors.Max(a => a.Count) : 0;
        MaxGenre = TopGenres.Count > 0 ? TopGenres.Max(g => g.Count) : 0;

        DueBack = dueBack.Dated;
        OpenEndedLoanCount = dueBack.OpenEndedCount;
    }

    public bool IsOverdue(DueBackRow row) => LoanDates.IsOverdue(row.DueOn, Today);
}
