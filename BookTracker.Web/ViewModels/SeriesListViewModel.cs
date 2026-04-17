using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class SeriesListViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public bool Loading { get; private set; } = true;
    public List<SeriesListItem> AllSeries { get; private set; } = [];

    public string SearchTerm { get; set; } = "";
    public string SelectedType { get; set; } = "";

    public async Task InitializeAsync()
    {
        await LoadSeriesAsync();
    }

    public async Task LoadSeriesAsync()
    {
        Loading = true;

        await using var db = await dbFactory.CreateDbContextAsync();

        IQueryable<Series> query = db.Series.Include(s => s.Books);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(s => s.Name.Contains(term) || (s.Author != null && s.Author.Contains(term)));
        }

        if (!string.IsNullOrEmpty(SelectedType) && Enum.TryParse<SeriesType>(SelectedType, out var type))
        {
            query = query.Where(s => s.Type == type);
        }

        AllSeries = await query
            .OrderBy(s => s.Name)
            .Select(s => new SeriesListItem(
                s.Id,
                s.Name,
                s.Author,
                s.Type,
                s.Books.Count,
                s.ExpectedCount
            ))
            .ToListAsync();

        Loading = false;
    }

    public async Task ApplyFiltersAsync()
    {
        await LoadSeriesAsync();
    }

    public async Task ClearFiltersAsync()
    {
        SearchTerm = "";
        SelectedType = "";
        await LoadSeriesAsync();
    }

    public static string CompletionText(SeriesListItem item)
    {
        if (item.Type == SeriesType.Series && item.ExpectedCount.HasValue)
            return $"{item.BookCount} / {item.ExpectedCount}";
        return $"{item.BookCount} books";
    }

    public static string CompletionBadgeClass(SeriesListItem item)
    {
        if (item.Type == SeriesType.Series && item.ExpectedCount.HasValue)
            return item.BookCount >= item.ExpectedCount.Value ? "bg-success" : "bg-warning text-dark";
        return "bg-light text-dark border";
    }

    public record SeriesListItem(
        int Id, string Name, string? Author, SeriesType Type,
        int BookCount, int? ExpectedCount);
}
