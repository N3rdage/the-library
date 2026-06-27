using BookTracker.Data.Models;

namespace BookTracker.Application.Formatting;

// Relocated from BookTracker.Web in PR6b-3 so the read-model handlers (which
// project BookFormat into its display string) can reach it. The 4th formatter
// to land in Application/Formatting after PR6a's two and PR6b-2's PartialDate.
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
