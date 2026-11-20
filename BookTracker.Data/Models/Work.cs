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
//
// Work's and WorkAuthor's fields are written only through the methods below
// (private setters; EF materialises through them regardless of accessibility),
// so a handler cannot bypass an invariant and the compiler enforces it. A Work is
// created through Create; the internal constructor serves the tests' seed builder
// (InternalsVisibleTo), and BookTracker.Application cannot reach it. Id stays
// public for EF and the tests' in-memory graphs. Its collections are read-only
// over private backing fields that EF reads and writes directly, so every
// change to them goes through the methods below.
public class Work
{
    private readonly List<WorkAuthor> _workAuthors = [];
    private readonly List<Author> _authors = [];
    private readonly List<Genre> _genres = [];
    private readonly List<Book> _books = [];
    private readonly List<BookWork> _bookWorks = [];

    // EF materialisation, and Create.
    private Work() { }

    /// <summary>Stores exactly the values given: nothing is trimmed or
    /// normalised here (unlike <see cref="UpdateDetails"/>), and the authorship
    /// rows keep the order and role they were built with, so a test seed gets
    /// exactly what it passes.</summary>
    internal Work(
        string title,
        string? subtitle,
        DateOnly? firstPublishedDate,
        DatePrecision firstPublishedDatePrecision,
        IEnumerable<WorkAuthor>? workAuthors,
        IEnumerable<Genre>? genres)
    {
        Title = title;
        Subtitle = subtitle;
        FirstPublishedDate = firstPublishedDate;
        FirstPublishedDatePrecision = firstPublishedDatePrecision;
        if (workAuthors is not null) _workAuthors.AddRange(workAuthors);
        if (genres is not null) _genres.AddRange(genres);
    }

    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; private set; } = string.Empty;

    [MaxLength(300)]
    public string? Subtitle { get; private set; }

    /// <summary>Explicit join with Order. The canonical read source for ordered
    /// display ("Preston & Child"). Written only through <see cref="AssignAuthorship"/>.</summary>
    public IReadOnlyCollection<WorkAuthor> WorkAuthors => _workAuthors;

    /// <summary>Skip-navigation through WorkAuthor — convenient for "any author of this work" semantics; does NOT preserve Order.</summary>
    public IReadOnlyCollection<Author> Authors => _authors;

    /// <summary>The year/date the Work was first published — distinct from any specific Edition's print date.</summary>
    public DateOnly? FirstPublishedDate { get; private set; }

    /// <summary>How precise <see cref="FirstPublishedDate"/> is — drives display formatting.</summary>
    public DatePrecision FirstPublishedDatePrecision { get; private set; } = DatePrecision.Day;

    /// <summary>Written only through <see cref="SetGenres"/> and
    /// <see cref="AdoptGenresFrom"/>.</summary>
    public IReadOnlyCollection<Genre> Genres => _genres;

    // Series membership is NOT a Work concept — it lives on the Book (the book
    // is installment N of a publication series). See Book.SeriesId.

    /// <summary>Skip-navigation over the Book↔Work join — "which books contain
    /// this work" semantics. Does NOT carry per-book display order. Written only
    /// through <see cref="LeaveAllBooks"/>.</summary>
    public IReadOnlyCollection<Book> Books => _books;

    /// <summary>Explicit join rows (the per-book <see cref="BookWork.Order"/>
    /// lives here). Mostly read from the Book side; present for symmetry and so
    /// <see cref="Book.AttachWork"/> can keep both ends of the graph consistent.
    /// Written only through <see cref="Book.AttachWork"/> and <see cref="RemoveFrom"/>.</summary>
    public IReadOnlyCollection<BookWork> BookWorks => _bookWorks;

    // --- Aggregate behaviour -------------------------------------------------
    // A Work is the durable creative unit; it OWNS its Book↔Work membership and
    // self-manages its lifecycle by ref count. Invariant: a Work appears in at
    // least one Book — it's created with its first book (the factory below) and
    // is orphaned (the caller deletes it) when its last book is removed. The
    // authorship invariant (≥1 contributor) lives on AssignAuthorship.
    // See docs/BACKEND-REFACTOR-DESIGN.md.

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
        // Route through the Book so the new work appends to the book's work
        // order (Order = max + 1) rather than landing at a default 0. The book
        // must have its BookWorks loaded for the max to be correct — a freshly
        // created book has none, which is correct (first work → Order 0).
        firstBook.AttachWork(work);
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

    /// <summary>Fills this Work's empty fields from <paramref name="other"/>
    /// without overwriting any value this Work already has: the subtitle, and
    /// the first-published date and its precision as a pair. Values are copied
    /// as stored. Returns how many of those two were filled (a work merge, where
    /// this Work is the winner).</summary>
    public int FillBlanksFrom(Work other)
    {
        var filled = 0;

        if (string.IsNullOrWhiteSpace(Subtitle) && !string.IsNullOrWhiteSpace(other.Subtitle))
        {
            Subtitle = other.Subtitle;
            filled++;
        }

        if (FirstPublishedDate is null && other.FirstPublishedDate is not null)
        {
            FirstPublishedDate = other.FirstPublishedDate;
            FirstPublishedDatePrecision = other.FirstPublishedDatePrecision;
            filled++;
        }

        return filled;
    }

    /// <summary>Associates this Work with another Book it also appears in. No-op
    /// if already associated. Returns true when newly attached. Delegates to
    /// <see cref="Book.AttachWork"/> so the work appends to the book's work order
    /// (the book must have its BookWorks loaded).</summary>
    public bool AppearsIn(Book book) => book.AttachWork(this);

    /// <summary>Removes this Work from a Book. Returns true when the Work now
    /// appears in no books (orphaned) and the caller should delete it. Operates
    /// on the explicit <see cref="BookWorks"/> join — the canonical membership
    /// collection since the per-book Order moved there — so callers load
    /// <c>Include(w =&gt; w.BookWorks)</c> (and the book's, for the shared-book case).</summary>
    public bool RemoveFrom(Book book)
    {
        var link = _bookWorks.FirstOrDefault(bw => bw.Book == book || (book.Id != 0 && bw.BookId == book.Id));
        if (link is not null)
        {
            _bookWorks.Remove(link);
            book.BookWorks.Remove(link);
        }
        return _bookWorks.Count == 0;
    }

    /// <summary>The Work's end of <see cref="Book.AttachWork"/>, which builds
    /// the join row and adds it to the Book's end.</summary>
    internal void AddBookWork(BookWork link) => _bookWorks.Add(link);

    /// <summary>Drops this Work from every Book in <see cref="Books"/> (the
    /// caller loads them), so EF deletes the join rows on save. The loser's
    /// side of a work merge, after its books have taken the winner.</summary>
    public void LeaveAllBooks() => _books.Clear();

    public void SetGenres(IReadOnlyList<Genre> genres)
    {
        _genres.Clear();
        foreach (var g in genres) _genres.Add(g);
    }

    /// <summary>Adds each of <paramref name="other"/>'s genres this Work does
    /// not already have, matched by id. Returns how many were added (a work
    /// merge, where this Work is the winner).</summary>
    public int AdoptGenresFrom(Work other)
    {
        var have = _genres.Select(g => g.Id).ToHashSet();
        var added = 0;
        foreach (var g in other.Genres.Where(g => !have.Contains(g.Id)).ToList())
        {
            _genres.Add(g);
            added++;
        }
        return added;
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

        _workAuthors.Clear();
        for (var i = 0; i < authors.Count; i++)
            _workAuthors.Add(new WorkAuthor(authors[i], i, AuthorRole.Author));

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
            _workAuthors.Add(new WorkAuthor(person, next, role));
            orderByRole[role] = next + 1;
        }
    }
}
