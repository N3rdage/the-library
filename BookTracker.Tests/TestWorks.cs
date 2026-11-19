using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for Work and WorkAuthor rows. Every seed builds through here, so
// Work's private setters need only these two bodies to reach the internal
// constructors (InternalsVisibleTo).
//
// The bodies store exactly what they are given, nothing trimmed or normalised
// (unlike Work.UpdateDetails),
// and an omitted argument leaves the entity default. The common case is one
// line, TestWorks.Work("Dune", author): one Author-role authorship at order 0.
// A seed with several authorships, other roles or no author at all passes
// `authorships:` instead (built with Authorship), or neither.
public static class TestWorks
{
    public static Work Work(
        string title = "",
        Author? author = null,
        string? subtitle = null,
        DateOnly? firstPublishedDate = null,
        DatePrecision firstPublishedDatePrecision = DatePrecision.Day,
        IEnumerable<WorkAuthor>? authorships = null,
        IEnumerable<Genre>? genres = null,
        int? id = null)
    {
        if (author is not null && authorships is not null)
            throw new ArgumentException("Pass either author or authorships, not both.");

        // Fully qualified: inside this class the simple name Work is the method.
        // The internal constructor stores everything as given, so a seed keeps
        // the exact order and role of each authorship it passes.
        var work = new global::BookTracker.Data.Models.Work(
            title,
            subtitle,
            firstPublishedDate,
            firstPublishedDatePrecision,
            author is not null ? new[] { Authorship(author) } : authorships,
            genres);

        if (id is not null) work.Id = id.Value;

        return work;
    }

    public static WorkAuthor Authorship(
        Author author,
        int order = 0,
        AuthorRole role = AuthorRole.Author)
    {
        return new WorkAuthor(author, order, role);
    }
}
