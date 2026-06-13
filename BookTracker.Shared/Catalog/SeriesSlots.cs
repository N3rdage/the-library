namespace BookTracker.Shared.Catalog;

/// <summary>
/// The single rule for series gap detection: which works occupy a real numbered
/// volume slot. A floored interquel ("4.5" — stored as <c>SeriesOrder</c> 4 with
/// <c>SeriesOrderDisplay</c> "4.5") shares an integer for sort adjacency but does
/// NOT own slot #4, so it must not mask a genuinely-missing numbered volume.
///
/// Centralised here rather than re-derived per consumer because the rule is
/// wire-coupled and crosses the project boundary: the web gap views
/// (<c>SharedParsers</c> AI profile, <c>WishlistViewModel</c> finite-series card)
/// and the offline mobile cache (<c>CatalogCache.GetSeriesGapsAsync</c>) all need
/// it, and mobile can't reference Web — Shared is the only common home. Keeping
/// the predicate in one place stops the three sites drifting (they already had:
/// mobile was missing the upper range clamp the web sites applied).
/// </summary>
public static class SeriesSlots
{
    /// <summary>
    /// True when the work occupies a real numbered volume slot: a positive
    /// plain-integer order with no display override. Interquels (display set),
    /// null orders, and non-positive orders all return false. Callers still
    /// apply the per-series upper bound (order ≤ ExpectedCount) themselves.
    /// </summary>
    public static bool OccupiesNumberedSlot(int? seriesOrder, string? seriesOrderDisplay) =>
        seriesOrderDisplay is null && seriesOrder is > 0;
}
