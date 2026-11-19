using BookTracker.Data.Models;

namespace BookTracker.Tests;

// Seed builder for Work and WorkAuthor rows. Every seed builds through here so
// that the next slice can make Work's setters private and change only these
// bodies, not some 250 call sites.
//
// The bodies are plain object initialisers for now: they store exactly what
// they are given, nothing trimmed or normalised (unlike Work.UpdateDetails),
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
        var work = new global::BookTracker.Data.Models.Work
        {
            Title = title,
            Subtitle = subtitle,
            FirstPublishedDate = firstPublishedDate,
            FirstPublishedDatePrecision = firstPublishedDatePrecision,
        };

        if (id is not null) work.Id = id.Value;
        if (author is not null) work.WorkAuthors = [Authorship(author)];
        if (authorships is not null) work.WorkAuthors = authorships.ToList();
        if (genres is not null) work.Genres = genres.ToList();

        return work;
    }

    public static WorkAuthor Authorship(
        Author author,
        int order = 0,
        AuthorRole role = AuthorRole.Author)
    {
        return new WorkAuthor { Author = author, Order = order, Role = role };
    }
}
