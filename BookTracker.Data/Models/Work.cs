using System.ComponentModel.DataAnnotations;

namespace BookTracker.Data.Models;

// A Work is the abstract creative unit — a story, novel, play, or poem.
// Multiple Books can contain the same Work (a Christie short story
// reprinted across several compendiums), and a single Book can contain
// multiple Works (a short-story collection).
//
// Authorship is many-to-many via the WorkAuthor join entity (PR2 of
// the multi-author cutover). Each WorkAuthor row points at the SPECIFIC
// Author entity used (Stephen King vs. the Richard Bachman alias) so the
// book is shown as actually published; aggregations roll aliases up via
// Author.CanonicalAuthorId. WorkAuthor.Order keeps the lead author first
// on display ("Preston & Child" stays in that order rather than being
// alphabetised).
public class Work
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Subtitle { get; set; }

    /// <summary>Explicit join with Order. The canonical read source for ordered display ("Preston & Child").</summary>
    public List<WorkAuthor> WorkAuthors { get; set; } = [];

    /// <summary>Skip-navigation through WorkAuthor — convenient for "any author of this work" semantics; does NOT preserve Order.</summary>
    public List<Author> Authors { get; set; } = [];

    /// <summary>The year/date the Work was first published — distinct from any specific Edition's print date.</summary>
    public DateOnly? FirstPublishedDate { get; set; }

    /// <summary>How precise <see cref="FirstPublishedDate"/> is — drives display formatting.</summary>
    public DatePrecision FirstPublishedDatePrecision { get; set; } = DatePrecision.Day;

    public List<Genre> Genres { get; set; } = [];

    // Series membership is NOT a Work concept — it lives on the Book (the book
    // is installment N of a publication series). See Book.SeriesId.

    public List<Book> Books { get; set; } = [];

    // --- Aggregate behaviour -------------------------------------------------
    // A Work is the durable creative unit; it OWNS its Book↔Work membership and
    // self-manages its lifecycle by ref count. Invariant: a Work appears in at
    // least one Book — it's created with its first book (the factory below) and
    // is orphaned (the caller deletes it) when its last book is removed. The
    // authorship invariant (≥1 contributor) lives on AssignAuthorship.
    // See docs/BACKEND-REFACTOR-DESIGN.md. (Collection setters stay public until
    // the C7 lock-down.)

    /// <summary>Creates a Work attached to its first Book (ref count starts at 1),
    /// with title, optional subtitle/date, and authorship. Title is required;
    /// authorship needs ≥1 contributor.</summary>
    public static Work Create(
        Book firstBook,
        string title,
        string? subtitle,
        DateOnly? firstPublished,
        DatePrecision firstPublishedPrecision,
        IReadOnlyList<Author> authors,
        IReadOnlyList<(Author Person, AuthorRole Role)>? contributors = null)
    {
        var work = new Work();
        work.UpdateDetails(title, subtitle);
        work.SetFirstPublished(firstPublished, firstPublishedPrecision);
        work.AssignAuthorship(authors, contributors);
        work.Books.Add(firstBook);
        return work;
    }

    public void UpdateDetails(string title, string? subtitle)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainRuleException("Title is required.");
        Title = title.Trim();
        Subtitle = subtitle.TrimToNull();
    }

    public void SetFirstPublished(DateOnly? date, DatePrecision precision)
    {
        FirstPublishedDate = date;
        FirstPublishedDatePrecision = precision;
    }

    /// <summary>Associates this Work with another Book it also appears in. No-op
    /// if already associated. Returns true when newly attached.</summary>
    public bool AppearsIn(Book book)
    {
        if (Books.Any(b => b.Id == book.Id)) return false;
        Books.Add(book);
        return true;
    }

    /// <summary>Removes this Work from a Book. Returns true when the Work now
    /// appears in no books (orphaned) and the caller should delete it.</summary>
    public bool RemoveFrom(Book book)
    {
        var existing = Books.FirstOrDefault(b => b.Id == book.Id);
        if (existing is not null) Books.Remove(existing);
        return Books.Count == 0;
    }

    public void SetGenres(IReadOnlyList<Genre> genres)
    {
        Genres.Clear();
        foreach (var g in genres) Genres.Add(g);
    }

    /// <summary>Rebuilds WorkAuthors: one Author-role row per author (Order 0+),
    /// then per-role contributor rows (each role its own Order sequence). Requires
    /// ≥1 contributor across both lists — editor-only Works (dictionaries) are
    /// valid. Authors/contributors must already be attached to the context
    /// (resolve via AuthorResolver first). Was AuthorResolver.AssignAuthors.</summary>
    public void AssignAuthorship(
        IReadOnlyList<Author> authors,
        IReadOnlyList<(Author Person, AuthorRole Role)>? additionalContributors = null)
    {
        var hasNonAuthor = additionalContributors is { Count: > 0 }
            && additionalContributors.Any(c => c.Role != AuthorRole.Author);
        if (authors.Count == 0 && !hasNonAuthor)
            throw new DomainRuleException("A work needs at least one contributor (author, editor, or other role).");

        WorkAuthors.Clear();
        for (var i = 0; i < authors.Count; i++)
            WorkAuthors.Add(new WorkAuthor { Author = authors[i], Order = i, Role = AuthorRole.Author });

        if (additionalContributors is null || additionalContributors.Count == 0) return;

        // Per-role Order sequencing + (Author, Role) dedup. Reference dedup works
        // because callers route every name through FindOrCreate* against one
        // context, so identical names are the same Author instance.
        var orderByRole = new Dictionary<AuthorRole, int>();
        var seen = new HashSet<(Author Person, AuthorRole Role)>();
        foreach (var (person, role) in additionalContributors)
        {
            if (role == AuthorRole.Author) continue;
            if (!seen.Add((person, role))) continue;
            var next = orderByRole.GetValueOrDefault(role, 0);
            WorkAuthors.Add(new WorkAuthor { Author = person, Order = next, Role = role });
            orderByRole[role] = next + 1;
        }
    }
}
