using BookTracker.Application;
using BookTracker.Application.Series;
using BookTracker.Data.Models;

namespace BookTracker.Web.ViewModels;

public class SeriesListViewModel(IDispatcher dispatcher)
{
    public bool Loading { get; private set; } = true;
    public IReadOnlyList<SeriesListItem> AllSeries { get; private set; } = [];

    public string SearchTerm { get; set; } = "";
    public string SelectedType { get; set; } = "";

    public async Task InitializeAsync()
    {
        await LoadSeriesAsync();
    }

    public async Task LoadSeriesAsync()
    {
        Loading = true;

        SeriesType? type =
            !string.IsNullOrEmpty(SelectedType) && Enum.TryParse<SeriesType>(SelectedType, out var t)
                ? t
                : null;

        AllSeries = await dispatcher.Query(new GetSeriesList(SearchTerm, type));

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
            return $"{item.WorkCount} / {item.ExpectedCount}";
        return $"{item.WorkCount} works";
    }

    public static string CompletionBadgeClass(SeriesListItem item)
    {
        if (item.Type == SeriesType.Series && item.ExpectedCount.HasValue)
            return item.WorkCount >= item.ExpectedCount.Value ? "bg-success" : "bg-warning text-dark";
        return "bg-light text-dark border";
    }
}
