using BookTracker.Application;
using BookTracker.Application.Series;
using Microsoft.Extensions.Logging;

namespace BookTracker.Web.Components.Shared;

/// <summary>
/// Shared eager find-or-create for a series by name → id (via the idempotent
/// <see cref="EnsureSeries"/> command), best-effort. Returns the id, or null on a
/// swallowed fault (the save's SeriesResolver net still creates the row). Mirrors
/// <see cref="PublisherEagerCreate"/> — extracted so the BookAdd and BulkAdd
/// series-accept paths share one copy of the dispatch + swallow-fault block.
///
/// Callers decide WHEN to call (an existing cached pick skips it); this owns only
/// the dispatch + swallow, not the membership check.
/// </summary>
internal static class SeriesEagerCreate
{
    public static async Task<int?> EnsureAsync(string name, IDispatcher dispatcher, ILogger logger)
    {
        try
        {
            return await dispatcher.Send(new EnsureSeries(name));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eager series create failed for {Name}; the save will create it", name);
            return null;
        }
    }
}
