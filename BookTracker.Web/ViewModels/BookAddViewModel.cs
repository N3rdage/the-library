using BookTracker.Application.Authors;
using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using BookTracker.Application.Formatting;

namespace BookTracker.Web.ViewModels;

public class BookAddViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup,
    SeriesMatchService seriesMatch,
    IWorkSearchService workSearch)
{
    /// <summary>Search existing Works for the collection-row autocomplete.
    /// Returns matches as the user types so they can attach an already-
    /// captured Work rather than re-typing it. excludeBookId is null —
    /// the Book is brand-new so there's nothing to exclude.</summary>
    public Task<IReadOnlyList<WorkSearchResult>> SearchExistingWorksAsync(string query, CancellationToken ct)
        => workSearch.SearchAsync(query, excludeBookId: null, ct: ct);


    public BookFormViewModel.BookFormInput BookInput { get; set; } = new();
    public WorkFormViewModel.WorkFormInput WorkInput { get; set; } = new();
    public EditionFormViewModel.EditionFormInput EditionInput { get; set; } = new();
    public CopyFormViewModel.CopyFormInput CopyInput { get; set; } = new();
    public List<string> LookupCandidates { get; private set; } = [];

    public string? LookupIsbn { get; set; }
    public string? LookupMessage { get; private set; }
    public bool LookingUp { get; private set; }
    public bool Saving { get; private set; }

    // No-ISBN flow (web only — for pre-1974 books that predate ISBN).
    // Toggling NoIsbnMode swaps the lookup panel from ISBN entry to a
    // title/author search that returns work-level candidates from Open
    // Library; selecting one prefills the form like an ISBN lookup would.
    public bool NoIsbnMode { get; set; }

    // Collection mode (web-prioritised): a single Book containing multiple
    // Works (e.g. "The Bachman Books", anthologies). Toggling on swaps the
    // single WorkForm for a repeatable Works builder; lookup fills only the
    // collection's Book.Title + cover and skips the work-specific fields.
    // Authors and genres are captured at save time via the SingleAuthor /
    // SingleGenre shared-mode toggles or per-row entry; series suggestions
    // stay deferred to per-work editing on /books/{id} because the
    // lookup flow describes the collection, not its constituent works.
    public bool IsCollection { get; set; }
    // Default to a single starter row. The Enter-on-Title affordance
    // grows the list as the user types; pre-seeding a second empty row
    // forced the user to manually re-enter authors there (the row
    // already existed before any author was typed, so the new-row
    // author inheritance didn't help). Start small, let typing grow it.
    public List<WorkFormViewModel.WorkFormInput> CollectionWorks { get; set; } = [new()];

    // "Same author(s) / genre(s) for all works" modes — when on, a single
    // shared chip-list at the top of the collection block applies to every
    // Work at save time and the per-row pickers are hidden. Common cases:
    //   - Author: single-author compendium (King's Different Seasons,
    //     Christie crime collections).
    //   - Genre: Drew's order-of-magnitude case — a 20-work anthology
    //     where 19 share a single genre and one differs.
    //
    // Toggle flips propagate data both ways so the user's picks survive
    // (no "where did my genres go" moment):
    //   - OFF → ON: shared = union of every row's per-row list (lossless
    //     capture; user can prune if the union has stragglers).
    //   - ON → OFF: every row's list := shared (broadcast; gives the user
    //     a pre-populated starting point on each row so they only need
    //     to edit the outliers — the order-of-magnitude flow).
    // Reset() bypasses these setters and clears via the backing fields so
    // it can't accidentally re-populate a fresh CollectionWorks row.
    private bool _singleAuthor;
    public bool SingleAuthor
    {
        get => _singleAuthor;
        set
        {
            if (_singleAuthor == value) return;
            if (value)
            {
                SharedAuthors = CollectionWorks
                    .SelectMany(r => r.Authors)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                // Fresh copy per row so picker mutations on one row don't
                // leak into another via shared list reference.
                foreach (var row in CollectionWorks)
                {
                    row.Authors = SharedAuthors.ToList();
                }
            }
            _singleAuthor = value;
        }
    }
    public List<string> SharedAuthors { get; set; } = [];

    private bool _singleGenre;
    public bool SingleGenre
    {
        get => _singleGenre;
        set
        {
            if (_singleGenre == value) return;
            if (value)
            {
                SharedGenreIds = CollectionWorks
                    .SelectMany(r => r.GenreIds)
                    .Distinct()
                    .ToList();
            }
            else
            {
                foreach (var row in CollectionWorks)
                {
                    row.GenreIds = SharedGenreIds.ToList();
                }
            }
            _singleGenre = value;
        }
    }
    public List<int> SharedGenreIds { get; set; } = [];

    public void AddCollectionWorkRow()
    {
        // New rows start empty. The previous behaviour (inherit from the
        // most recent populated row) predated the SingleAuthor /
        // SingleGenre toggles and is wrong now that those exist:
        //   - SingleAuthor ON  -> per-row Author field doesn't render;
        //     inheritance is irrelevant.
        //   - SingleAuthor OFF -> user has explicitly flagged "different
        //     author per row"; inheritance is exactly the wrong default
        //     (every new row carries the previous author and the user
        //     must remove-then-add).
        // Same shape for SingleGenre / GenreIds.
        CollectionWorks.Add(new WorkFormViewModel.WorkFormInput());
    }

    public void RemoveCollectionWorkRow(int index)
    {
        if (index < 0 || index >= CollectionWorks.Count) return;
        CollectionWorks.RemoveAt(index);
        if (CollectionWorks.Count == 0) CollectionWorks.Add(new());
    }

    /// <summary>True if any collection row is in attach-existing mode —
    /// used by the page to decide whether to surface a confirm dialog
    /// when the user flicks IsCollection back off (existing-work
    /// attachments would be silently discarded otherwise).</summary>
    public bool HasAttachedWorkRows => CollectionWorks.Any(r => r.AttachedWorkId is not null);

    /// <summary>Marks a collection row as "attach this existing Work to
    /// the new Book" instead of creating a new one. The row's editable
    /// fields are kept (so reverting is non-destructive) but ignored at
    /// save time; the UI hides them in favour of a compact summary.</summary>
    public void AttachExistingToRow(int rowIndex, WorkSearchResult picked)
    {
        if (rowIndex < 0 || rowIndex >= CollectionWorks.Count) return;
        var row = CollectionWorks[rowIndex];
        row.AttachedWorkId = picked.Id;
        row.AttachedWorkAuthor = picked.AuthorName;
        // Mirror the existing Work's title into the row's title so subsequent
        // re-opens of the page or save-then-edit flows render the right
        // string. Title still flows through `row.Title` either way.
        row.Title = picked.Title;
    }

    /// <summary>Undo an existing-work attach on a row — clears the
    /// AttachedWorkId so the row returns to editable new-work mode.
    /// Preserves whatever title the user had typed before picking.</summary>
    public void DetachRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= CollectionWorks.Count) return;
        CollectionWorks[rowIndex].AttachedWorkId = null;
        CollectionWorks[rowIndex].AttachedWorkAuthor = null;
    }
    public string? SearchTitle { get; set; }
    public string? SearchAuthor { get; set; }
    public IReadOnlyList<BookSearchCandidate> SearchCandidates { get; private set; } = [];
    public bool Searching { get; private set; }
    public string? SearchMessage { get; private set; }

    public SeriesMatch? SeriesSuggestion { get; private set; }
    public bool SeriesSuggestionDismissed { get; set; }

    // Acceptance state for the series suggestion banner. When the user clicks
    // Accept, we capture the suggestion's identity (existing SeriesId, or a
    // proposed name to find-or-create on save) plus the suggested order. The
    // save path reads these and attaches the Work to the right Series row.
    // Cleared on Reset() and on a fresh successful lookup so accept-state
    // can't bleed across captures.
    public bool SeriesSuggestionAccepted { get; private set; }
    public int? AcceptedSeriesId { get; private set; }
    public string? AcceptedSeriesName { get; private set; }
    // Single round-trippable order label captured at accept time ("5" or
    // "4.5"); re-parsed into (SeriesOrder, SeriesOrderDisplay) at save so the
    // stored int can't skew from the parser. Null when the suggestion carries
    // no order.
    public string? AcceptedSeriesOrderLabel { get; private set; }

    public void AcceptSeriesSuggestion()
    {
        if (SeriesSuggestion is null) return;
        // Only API-sourced suggestions (Existing or NewSeries) are actionable —
        // the local-fallback paths name no concrete series to attach. The UI
        // should only render an Accept button for those reasons.
        if (SeriesSuggestion.Reason is not (MatchReason.ApiMatchExisting or MatchReason.ApiMatchNewSeries))
        {
            return;
        }
        AcceptedSeriesId = SeriesSuggestion.SeriesId;
        AcceptedSeriesName = SeriesSuggestion.SeriesName;
        AcceptedSeriesOrderLabel = SeriesOrderParser.Format(SeriesSuggestion.SuggestedOrder, SeriesSuggestion.SuggestedOrderDisplay);
        SeriesSuggestionAccepted = true;
    }

    public void UndoSeriesSuggestionAccept()
    {
        SeriesSuggestionAccepted = false;
        AcceptedSeriesId = null;
        AcceptedSeriesName = null;
        AcceptedSeriesOrderLabel = null;
    }

    // Existing-book detection — set during LookupAsync when the ISBN
    // already maps to an Edition in the library. The Add page surfaces a
    // banner offering "add another copy" / "edit existing" instead of
    // letting the user accidentally hit the unique-ISBN constraint by
    // saving a duplicate.
    public ExistingBookMatch? ExistingBook { get; private set; }
    public bool AddingCopy { get; private set; }

    public async Task LookupAsync()
    {
        LookupMessage = null;
        ExistingBook = null;
        if (string.IsNullOrWhiteSpace(LookupIsbn))
        {
            LookupMessage = "Enter an ISBN to look up.";
            return;
        }

        LookingUp = true;
        try
        {
            // Existing-edition check first — if the ISBN already maps to a
            // Book in the library, surface "add another copy" instead of
            // letting the user save a duplicate that would hit the unique
            // ISBN constraint.
            var cleanIsbn = new string(LookupIsbn.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                var existing = await db.Editions
                    .Include(e => e.Book)
                        .ThenInclude(b => b.Works).ThenInclude(w => w.WorkAuthors).ThenInclude(wa => wa.Author)
                    .Include(e => e.Copies)
                    .FirstOrDefaultAsync(e => e.Isbn == cleanIsbn);

                if (existing is not null)
                {
                    ExistingBook = new ExistingBookMatch(
                        existing.Book.Id,
                        existing.Id,
                        existing.Book.Title,
                        string.Join(", ", existing.Book.Works
                            .SelectMany(w => w.WorkAuthors.Where(wa => wa.Role == AuthorRole.Author).OrderBy(wa => wa.Order).Select(wa => wa.Author.Name))
                            .Distinct()),
                        existing.Copies.Count);
                    LookupMessage = null;
                    return;
                }
            }

            var result = await lookup.LookupByIsbnAsync(LookupIsbn, CancellationToken.None);
            if (result is null)
            {
                LookupMessage = $"No match found for ISBN {LookupIsbn}.";
                return;
            }

            // The Add page creates a Book with one Work. Lookup result
            // populates both: Book.Title mirrors the Work title, plus
            // cover; Work gets title/subtitle/author/genres. In Collection
            // mode the work-specific fields are skipped because the lookup
            // result describes the *collection*, not its constituent works.
            if (string.IsNullOrWhiteSpace(BookInput.Title)) BookInput.Title = result.Title ?? "";
            if (string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl)) BookInput.DefaultCoverArtUrl = result.CoverUrl;
            if (!IsCollection)
            {
                if (string.IsNullOrWhiteSpace(WorkInput.Title)) WorkInput.Title = result.Title ?? "";
                if (string.IsNullOrWhiteSpace(WorkInput.Subtitle)) WorkInput.Subtitle = result.Subtitle;
                // Lookup gives a single author string — seed the chip list with it
                // when empty. User can add additional co-authors via the picker
                // before saving.
                if (WorkInput.Authors.Count == 0 && !string.IsNullOrWhiteSpace(result.Author))
                {
                    WorkInput.Authors = [result.Author];
                }
            }
            if (string.IsNullOrWhiteSpace(EditionInput.Isbn)) EditionInput.Isbn = result.Isbn;
            if (string.IsNullOrWhiteSpace(EditionInput.Publisher)) EditionInput.Publisher = result.Publisher;
            if (string.IsNullOrWhiteSpace(EditionInput.DatePrinted) && result.DatePrinted is DateOnly d)
            {
                EditionInput.DatePrinted = PartialDateParser.Format(d, result.DatePrintedPrecision);
            }

            // Genre candidates and series suggestions are work-level; not
            // meaningful in Collection mode where each work has its own.
            if (!IsCollection)
            {
                LookupCandidates = result.GenreCandidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                // Picker reads LookupCandidates via parameter binding; the
                // page calls picker.ApplyCandidatesAsync explicitly after
                // this method returns so the fuzzy-match auto-select runs
                // once per lookup (not on every re-render — keeps manual
                // genre removals sticky).

                SeriesSuggestion = await seriesMatch.FindMatchAsync(result);
                SeriesSuggestionDismissed = false;
                // Fresh lookup → clear any acceptance carried over from a prior
                // ISBN attempt in this session (e.g. user typed wrong ISBN,
                // accepted a Discworld suggestion, then corrected to a non-series
                // book — the prior accept must not silently apply).
                UndoSeriesSuggestionAccept();
            }
            else
            {
                LookupCandidates = [];
                SeriesSuggestion = null;
                SeriesSuggestionDismissed = false;
                UndoSeriesSuggestionAccept();
            }

            LookupMessage = $"Prefilled from {result.Source}. Edit anything before saving.";
        }
        finally
        {
            LookingUp = false;
        }
    }

    /// <summary>
    /// Adds a new Copy to the Edition flagged in <see cref="ExistingBook"/>.
    /// Used by the "you already own this book" banner so re-scanning a
    /// barcode for a second physical copy is a one-click action.
    /// </summary>
    /// <returns>The book id of the existing book the copy was attached to.</returns>
    public async Task<int?> AddCopyToExistingAsync()
    {
        if (ExistingBook is null) return null;

        AddingCopy = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var newCopy = new Copy
            {
                EditionId = ExistingBook.EditionId,
                Condition = CopyInput.Condition,
            };
            db.Copies.Add(newCopy);
            await db.SaveChangesAsync();
            return ExistingBook.BookId;
        }
        finally
        {
            AddingCopy = false;
        }
    }

    /// <summary>Resets all input state so the page is ready for the next book.</summary>
    public void Reset()
    {
        BookInput = new();
        WorkInput = new();
        EditionInput = new();
        CopyInput = new();
        LookupIsbn = null;
        LookupMessage = null;
        LookupCandidates = [];
        ExistingBook = null;
        SeriesSuggestion = null;
        SeriesSuggestionDismissed = false;
        UndoSeriesSuggestionAccept();
        SearchTitle = null;
        SearchAuthor = null;
        SearchCandidates = [];
        SearchMessage = null;
        NoIsbnMode = false;
        IsCollection = false;
        CollectionWorks = [new()];
        // Bypass the SingleAuthor / SingleGenre setters here — they
        // propagate shared list state to CollectionWorks rows on flip,
        // which would re-populate the just-replaced empty starter row
        // with whatever the previous capture had in shared mode.
        _singleAuthor = false;
        SharedAuthors = [];
        _singleGenre = false;
        SharedGenreIds = [];
        // Picker state clears via parameter binding when the page resets
        // its selectedGenreIds + this VM's LookupCandidates above. No
        // direct picker poke needed.
    }

    public record ExistingBookMatch(int BookId, int EditionId, string Title, string Author, int CopyCount);

    public async Task SearchAsync()
    {
        SearchMessage = null;
        SearchCandidates = [];

        var t = SearchTitle?.Trim();
        var a = SearchAuthor?.Trim();
        if (string.IsNullOrEmpty(t) && string.IsNullOrEmpty(a))
        {
            SearchMessage = "Enter a title or author to search.";
            return;
        }

        Searching = true;
        try
        {
            SearchCandidates = await lookup.SearchByTitleAuthorAsync(t, a, CancellationToken.None);
            if (SearchCandidates.Count == 0)
            {
                SearchMessage = "No matches found. Try different keywords or fill the form manually.";
            }
        }
        finally
        {
            Searching = false;
        }
    }

    public async Task ApplyCandidateAsync(BookSearchCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(BookInput.Title)) BookInput.Title = candidate.Title ?? "";
        if (string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl)) BookInput.DefaultCoverArtUrl = candidate.CoverUrl;
        if (string.IsNullOrWhiteSpace(WorkInput.Title)) WorkInput.Title = candidate.Title ?? "";
        if (WorkInput.Authors.Count == 0 && !string.IsNullOrWhiteSpace(candidate.Author))
        {
            WorkInput.Authors = [candidate.Author];
        }
        // first_publish_year is the WORK's first year — perfect fit for
        // Work.FirstPublishedDate; not the edition's print date. We only
        // know the year so format it as such.
        if (string.IsNullOrWhiteSpace(WorkInput.FirstPublishedDate) && candidate.FirstPublishYear is int year)
        {
            WorkInput.FirstPublishedDate = year.ToString();
        }

        SearchMessage = $"Prefilled from Open Library. Fill in format, exact print date, and publisher from the book in hand.";

        SeriesSuggestion = await seriesMatch.FindMatchAsync(candidate.Title, candidate.Author);
        SeriesSuggestionDismissed = false;
        UndoSeriesSuggestionAccept();

        // No genre auto-pick for the no-ISBN flow — search results don't
        // carry subjects. User selects genres manually via the picker.
    }

    public async Task<int?> SaveAsync(List<int> selectedGenreIds)
    {
        Saving = true;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            var selectedGenres = await db.Genres
                .Where(g => selectedGenreIds.Contains(g.Id))
                .ToListAsync();

            var publisher = await PublisherResolver.ResolveAsync(db, EditionInput.Publisher);

            List<Work> works;
            if (IsCollection)
            {
                // Collection mode: build N Works, one per row. Rows split
                // into two flavours:
                //   - attach-existing rows (AttachedWorkId set): load the
                //     existing Work from the DB and attach it; the inline
                //     edit fields are not used.
                //   - new-work rows (AttachedWorkId null): create a Work
                //     from the row fields (title, authors, subtitle,
                //     first-published, per-row or shared genres).
                //
                // Series suggestions stay deferred to per-work editing on
                // /books/{id} (the lookup flow can't pick per-work, and
                // applying to all would be wrong). Author source depends on
                // SingleAuthor mode; genre source on SingleGenre — both
                // apply only to new-work rows (existing Works bring their
                // own).
                List<string> AuthorsFor(WorkFormViewModel.WorkFormInput row) =>
                    SingleAuthor ? SharedAuthors : row.Authors;
                List<int> GenresFor(WorkFormViewModel.WorkFormInput row) =>
                    SingleGenre ? SharedGenreIds : row.GenreIds;

                // Preserve the user-entered order across both row flavours so
                // the saved Book.Works reflects the order on screen.
                var orderedRows = CollectionWorks
                    .Where(w => w.AttachedWorkId is not null
                                || (!string.IsNullOrWhiteSpace(w.Title) && AuthorsFor(w).Count > 0))
                    .ToList();
                if (orderedRows.Count == 0)
                {
                    throw new InvalidOperationException(SingleAuthor
                        ? "A collection in Single-Author mode needs at least one work with a title, plus at least one shared author. (Or attach an existing work.)"
                        : "A collection must contain at least one work with a title and an author. (Or attach an existing work.)");
                }

                var newRows = orderedRows.Where(r => r.AttachedWorkId is null).ToList();
                var attachIds = orderedRows
                    .Where(r => r.AttachedWorkId is int)
                    .Select(r => r.AttachedWorkId!.Value)
                    .Distinct()
                    .ToList();

                // Resolve the union of distinct author names across new-work
                // rows in one pass — calling FindOrCreate per row would
                // create duplicate Author entities when the same name
                // appears in multiple stories (the existence check queries
                // the committed DB, missing pending entities in the change
                // tracker).
                var allNames = SingleAuthor ? SharedAuthors : newRows.SelectMany(r => r.Authors);
                // Union the contributor names into the same FindOrCreateAll pass
                // so a person credited as Author in one row and Editor in another
                // resolves to a single Author entity (the existence check queries
                // committed rows only — per-row FindOrCreate would insert dupes).
                var contributorNames = newRows.SelectMany(r => r.Contributors.Select(c => c.Name));
                var allAuthors = await AuthorResolver.FindOrCreateAllAsync(allNames.Concat(contributorNames), db);
                var byName = allAuthors.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

                // Resolve the union of distinct genre ids across new-work
                // rows in one query.
                var allGenreIds = (SingleGenre
                    ? SharedGenreIds
                    : newRows.SelectMany(r => r.GenreIds))
                    .Distinct()
                    .ToList();
                var collectionGenres = allGenreIds.Count == 0
                    ? new List<Genre>()
                    : await db.Genres.Where(g => allGenreIds.Contains(g.Id)).ToListAsync();
                var genresById = collectionGenres.ToDictionary(g => g.Id);

                // Load the attach-existing Works in one query. Missing ids
                // are silently dropped — they can only happen if the Work
                // was deleted between picking it in the dialog and saving;
                // failing the whole save over that would be unhelpful.
                var attachedWorks = attachIds.Count == 0
                    ? new List<Work>()
                    : await db.Works.Where(w => attachIds.Contains(w.Id)).ToListAsync();
                var attachedById = attachedWorks.ToDictionary(w => w.Id);

                works = new List<Work>(orderedRows.Count);
                foreach (var row in orderedRows)
                {
                    if (row.AttachedWorkId is int existingId)
                    {
                        if (attachedById.TryGetValue(existingId, out var existing))
                        {
                            works.Add(existing);
                        }
                        continue;
                    }

                    var rowAuthors = AuthorsFor(row)
                        .Select(n => n?.Trim())
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(n => byName[n!])
                        .ToList();
                    var rowContributors = row.Contributors
                        .Where(c => !string.IsNullOrWhiteSpace(c.Name) && byName.ContainsKey(c.Name.Trim()))
                        .Select(c => (Person: byName[c.Name.Trim()], c.Role))
                        .ToList();
                    if (rowAuthors.Count == 0 && rowContributors.Count == 0)
                    {
                        throw new InvalidOperationException($"Each work in the collection needs at least one contributor (work \"{row.Title}\" had none).");
                    }
                    var rowGenres = GenresFor(row)
                        .Distinct()
                        .Where(genresById.ContainsKey)
                        .Select(id => genresById[id])
                        .ToList();
                    var rowFirstPub = PartialDateParser.TryParse(row.FirstPublishedDate) ?? PartialDate.Empty;
                    var w = new Work
                    {
                        Title = row.Title!.Trim(),
                        Subtitle = string.IsNullOrWhiteSpace(row.Subtitle) ? null : row.Subtitle!.Trim(),
                        FirstPublishedDate = rowFirstPub.Date,
                        FirstPublishedDatePrecision = rowFirstPub.Precision,
                        Genres = rowGenres,
                    };
                    w.AssignAuthorship(rowAuthors, rowContributors);
                    works.Add(w);
                }
            }
            else
            {
                var authors = await AuthorResolver.FindOrCreateAllAsync(WorkInput.Authors, db);
                var contributors = await ResolveContributorsAsync(WorkInput.Contributors, db);
                if (authors.Count == 0 && contributors.Count == 0)
                {
                    throw new InvalidOperationException("At least one contributor (author, editor, or other role) is required to save a Work.");
                }
                var firstPub = PartialDateParser.TryParse(WorkInput.FirstPublishedDate) ?? PartialDate.Empty;
                var work = new Work
                {
                    Title = (WorkInput.Title ?? BookInput.Title)!.Trim(),
                    Subtitle = string.IsNullOrWhiteSpace(WorkInput.Subtitle) ? null : WorkInput.Subtitle!.Trim(),
                    FirstPublishedDate = firstPub.Date,
                    FirstPublishedDatePrecision = firstPub.Precision,
                    Genres = selectedGenres,
                };
                work.AssignAuthorship(authors, contributors);

                // Attach to the accepted series, if any. AcceptedSeriesId points at
                // an existing local Series row; AcceptedSeriesName (without an Id)
                // means the upstream API named a series we don't have yet — find-
                // or-create it by name. Default new series to SeriesType.Series
                // (numbered) per the Q1/Q2 defaults — user can flip to Collection
                // on /series/{id} later if wrong.
                if (SeriesSuggestionAccepted)
                {
                    // Derive the (sort int, display) pair from the captured label
                    // at save time — never freeze the int at accept time.
                    var (acceptedOrder, acceptedOrderDisplay) = SeriesOrderParser.Parse(AcceptedSeriesOrderLabel);
                    if (AcceptedSeriesId is int existingId)
                    {
                        work.SeriesId = existingId;
                        work.SeriesOrder = acceptedOrder;
                        work.SeriesOrderDisplay = acceptedOrderDisplay;
                    }
                    else if (!string.IsNullOrWhiteSpace(AcceptedSeriesName))
                    {
                        work.Series = await SeriesResolver.ResolveAsync(db, AcceptedSeriesName);
                        work.SeriesOrder = acceptedOrder;
                        work.SeriesOrderDisplay = acceptedOrderDisplay;
                    }
                }

                works = [work];
            }

            var datePrinted = PartialDateParser.TryParse(EditionInput.DatePrinted) ?? PartialDate.Empty;
            var book = new Book
            {
                Title = BookInput.Title!.Trim(),
                Notes = string.IsNullOrWhiteSpace(BookInput.Notes) ? null : BookInput.Notes.Trim(),
                Category = BookInput.Category,
                Status = BookInput.Status,
                Rating = BookInput.Rating,
                DefaultCoverArtUrl = string.IsNullOrWhiteSpace(BookInput.DefaultCoverArtUrl) ? null : BookInput.DefaultCoverArtUrl.Trim(),
                Works = works,
                Editions =
                [
                    new Edition
                    {
                        Isbn = string.IsNullOrWhiteSpace(EditionInput.Isbn) ? null : EditionInput.Isbn.Trim(),
                        Format = EditionInput.Format,
                        EditionNumber = EditionInput.EditionNumber,
                        DatePrinted = datePrinted.Date,
                        DatePrintedPrecision = datePrinted.Precision,
                        Publisher = publisher,
                        CoverUrl = string.IsNullOrWhiteSpace(EditionInput.CoverUrl) ? null : EditionInput.CoverUrl.Trim(),
                        Copies = [new Copy { Condition = CopyInput.Condition }]
                    }
                ]
            };

            db.Books.Add(book);
            await db.SaveChangesAsync();
            return book.Id;
        }
        finally
        {
            Saving = false;
        }
    }

    // Resolve non-Author contributor entries through FindOrCreateAsync so
    // brand-new names get an Author row, and pair each with its picked Role.
    // Blank-name rows are skipped silently — the picker permits an empty
    // pending row to coexist with chips.
    private static async Task<List<(Author Person, AuthorRole Role)>> ResolveContributorsAsync(
        List<ContributorEntry> entries,
        BookTrackerDbContext db)
    {
        var result = new List<(Author, AuthorRole)>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) continue;
            var author = await AuthorResolver.FindOrCreateAsync(entry.Name, db);
            result.Add((author, entry.Role));
        }
        return result;
    }
}
