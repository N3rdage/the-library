using BookTracker.Data.Models;

namespace BookTracker.Web;

public static class BookFormatExtensions
{
    public static string DisplayName(this BookFormat format) => format switch
    {
        BookFormat.Hardcover => "Hardcover",
        BookFormat.TradePaperback => "Trade Paperback",
        BookFormat.MassMarketPaperback => "Mass Market Paperback",
        BookFormat.LargePrint => "Large Print",
        _ => format.ToString(),
    };
}
