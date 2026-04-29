using BookTracker.Data.Models;

namespace BookTracker.Web.Services;

public record BookLookupResult(
    string Isbn,
    string? Title,
    string? Subtitle,
    string? Author,
    string? Publisher,
    IReadOnlyList<string> GenreCandidates,
    DateOnly? DatePrinted,
    string? CoverUrl,
    string Source,
    BookFormat? Format = null,
    DatePrecision DatePrintedPrecision = DatePrecision.Day,
    // Series enrichment (TODO #17). Open Library populates the name + order
    // from its `series` field; Google Books is skipped because its series
    // metadata doesn't expose a reliable series name (only opaque seriesId).
    // SeriesNumber is the integer order if it parsed cleanly; SeriesNumberRaw
    // is the original publisher string (e.g. "5.5", "Vol 1A") used when the
    // suggestion message wants to surface the source value even though it
    // can't be stored in `Work.SeriesOrder` (currently `int?`). The non-int
    // case is tracked as a follow-up TODO ("Support non-integer SeriesOrder").
    string? Series = null,
    int? SeriesNumber = null,
    string? SeriesNumberRaw = null);
