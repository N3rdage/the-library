using BookTracker.Application;
using BookTracker.Application.Shelf;

namespace BookTracker.Web.ViewModels;

// Display VM for /shelf-order: dispatches GetShelfOrder and formats each row's
// trailing detail. The grouping and order are decided in Application.
public class ShelfOrderViewModel(IDispatcher dispatcher) : DispatchingViewModel
{
    public bool Loading { get; private set; } = true;
    public ShelfOrderView? View { get; private set; }

    public async Task InitializeAsync()
    {
        Loading = true;
        View = await dispatcher.Query(new GetShelfOrder());
        Loading = false;
    }

    // "(Discworld #4, 1987)", "(1987)", "(Discworld)" or "" when there is neither.
    public static string Detail(ShelfBook book)
    {
        var series = book.SeriesName is null
            ? null
            : book.SeriesOrderLabel is null ? book.SeriesName : $"{book.SeriesName} #{book.SeriesOrderLabel}";
        var parts = new[] { series, book.FirstPublishedYear?.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            .Where(p => p is not null);
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? "" : $"({joined})";
    }
}
