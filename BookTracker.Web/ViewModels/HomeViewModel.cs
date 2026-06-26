using BookTracker.Application;
using BookTracker.Application.Home;

namespace BookTracker.Web.ViewModels;

public class HomeViewModel(IDispatcher dispatcher)
{
    public int TotalBooks { get; private set; }
    public int TotalAuthors { get; private set; }
    public IReadOnlyList<AuthorCount> TopAuthors { get; private set; } = [];
    public IReadOnlyList<GenreCount> TopGenres { get; private set; } = [];
    public int MaxAuthor { get; private set; }
    public int MaxGenre { get; private set; }

    public async Task InitializeAsync()
    {
        var dashboard = await dispatcher.Query(new GetHomeDashboard());

        TotalBooks = dashboard.TotalBooks;
        TotalAuthors = dashboard.TotalAuthors;
        TopAuthors = dashboard.TopAuthors;
        TopGenres = dashboard.TopGenres;

        // Bar-scaling maxima are presentation-derived from the lists.
        MaxAuthor = TopAuthors.Count > 0 ? TopAuthors.Max(a => a.Count) : 0;
        MaxGenre = TopGenres.Count > 0 ? TopGenres.Max(g => g.Count) : 0;
    }
}
