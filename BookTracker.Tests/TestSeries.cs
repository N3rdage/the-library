using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for Series rows. Every seed builds through here, so Series'
// private setters need only this body to reach the internal constructor
// (InternalsVisibleTo).
//
// The body stores exactly what it is given, nothing trimmed or normalised
// (unlike Series.UpdateDetails, which also drops a Collection's ExpectedCount),
// and an omitted argument leaves the entity default. The common case is one
// line, TestSeries.Series("Discworld", SeriesType.Collection).
public static class TestSeries
{
    public static Series Series(
        string name,
        SeriesType type = SeriesType.Series,
        string? author = null,
        int? expectedCount = null,
        string? description = null)
    {
        // Fully qualified: inside this class the simple name Series is the method.
        return new global::BookTracker.Data.Models.Series(name, type, author, expectedCount, description);
    }
}
