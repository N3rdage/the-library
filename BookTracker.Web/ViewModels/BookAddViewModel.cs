using BookTracker.Application;
using BookTracker.Application.Books;
using BookTracker.Application.Series;
using BookTracker.Application.Works;
using BookTracker.Web.Services;
using BookTracker.Web.Services.Covers;
using BookTracker.Web.Validation;
using BookTracker.Web.Components.Shared;
using Microsoft.Extensions.Logging;
using BookTracker.Application.Formatting;

namespace BookTracker.Web.ViewModels;

public class BookAddViewModel(
    IBookLookupService lookup,
    SeriesMatchService seriesMatch,
    IDispatcher dispatcher,
    ILogger<BookAddViewModel> logger) : DispatchingViewModel
{
    /// <summary>Search existing Works for the collection-row autocomplete.
    /// Returns matches as the user types so they can attach an already-
    /// captured Work rather than re-typing it. excludeBookId is null —
    /// the Book is brand-new so there's nothing to exclude.</summary>
    public Task<IReadOnlyList<WorkSearchResult>> SearchExistingWorksAsync(string query, CancellationToken ct)
        => dispatcher.Query(new SearchWorks(query, ExcludeBookId: null, Max: 20), ct);


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

    // The chosen series — one source of truth for BOTH the ISBN-suggestion
    // "Use this" path and the manual CreatableAutocomplete typeahead. Written
    // only via AcceptSeriesSuggestion / OnSeriesChosenAsync / ClearSeriesSelection,
    // so the name, the resolved id, and the order can't drift apart (the bug the
    // old shared-mutable-state + separate accepted-flag design kept producing).
    // The save reads these to attach the Work to the right Series row; "chosen vs
    // not" is simply AcceptedSeriesName being non-blank — there is no separate flag.
    public int? AcceptedSeriesId { get; private set; }
    public string? AcceptedSeriesName { get; private set; }
    // Single round-trippable order label ("5" or "4.5"); re-parsed into
    // (SeriesOrder, SeriesOrderDisplay) at save so the stored int can't skew from
    // the parser. Null when no order. Bound by the order field for manual edits.
    public string? AcceptedSeriesOrderLabel { get; set; }

    // Existing series cached client-side (loaded on init) for the manual
    // typeahead: filtered in-memory for suggestions, and consulted on commit to
    // tell an existing pick (no create call) from a genuinely new name.
    public List<SeriesOption> ExistingSeries { get; private set; } = [];
    public record SeriesOption(int Id, string Name);

    public async Task AcceptSeriesSuggestion()
    {
        if (SeriesSuggestion is null) return;
        // Only API-sourced suggestions (Existing or NewSeries) are actionable —
        // the local-fallback paths name no concrete series to attach. The UI
        // should only render an Accept button for those reasons.
        if (SeriesSuggestion.Reason is not (MatchReason.ApiMatchExisting or MatchReason.ApiMatchNewSeries))
        {
            return;
        }
        // "Use this" fills the single chosen-series state (name + order); the
        // typeahead reflects it via its bound Value. Once filled, the suggestion
        // prompt hides (it's gated on AcceptedSeriesName being blank), so there's
        // no separate green/accepted flag to drift out of sync with the field.
        AcceptedSeriesId = SeriesSuggestion.SeriesId;
        AcceptedSeriesName = SeriesSuggestion.SeriesName;
        AcceptedSeriesOrderLabel = SeriesOrderParser.Format(SeriesSuggestion.SuggestedOrder, SeriesSuggestion.SuggestedOrderDisplay);

        // "Use this" is the commit gesture (a discrete button click), so eager-create
        // a genuinely-new series right now (TD-15a) and pin its id — the save then
        // attaches by id instead of find-or-creating. Best-effort: on failure the
        // id stays null and the save's SeriesResolver net still creates it by name.
        if (AcceptedSeriesId is null && !string.IsNullOrWhiteSpace(AcceptedSeriesName))
        {
            AcceptedSeriesId = await EagerEnsureSeriesAsync(AcceptedSeriesName);
        }
    }

    // Clears the chosen-series selection — used on a fresh lookup and Reset so a
    // prior choice can't bleed across captures, and reached from the UI via the
    // typeahead's Clearable X (OnSeriesChosenAsync(null)).
    private void ClearSeriesSelection()
    {
        AcceptedSeriesId = null;
        AcceptedSeriesName = null;
        AcceptedSeriesOrderLabel = null;
    }

    /// <summary>Loads the existing-series cache for the manual series typeahead.
    /// Called once from the page's OnInitializedAsync.</summary>
    public async Task InitializeAsync()
    {
        var options = await dispatcher.Query(new GetSeriesOptions());
        ExistingSeries = options.Select(s => new SeriesOption(s.Id, s.Name)).ToList();
    }

    public Task<IEnumerable<string>> SearchSeriesAsync(string? query, CancellationToken ct)
        => Task.FromResult(LookupSearch.Filter(ExistingSeries.Select(s => s.Name), query));

    // The ONLY commit point for the manual series typeahead: an explicit
    // selection (an existing pick, the "Add …" row, or the Clearable X → null).
    // CoerceValue="false" on the CreatableAutocomplete means typing alone never
    // reaches here — no per-keystroke work, no blur guessing at "is the user
    // finished". Resolve the chosen name to an id: an existing cached name
    // attaches with no create round-trip; a genuinely new name is eager-created
    // (TD-15a) and cached. Choosing a DIFFERENT series drops the order (it
    // belonged to the old one — it must not bleed onto the new series); a blank
    // selection clears the whole choice.
    public async Task OnSeriesChosenAsync(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) { ClearSeriesSelection(); return; }

        var changedSeries = !string.Equals(trimmed, AcceptedSeriesName, StringComparison.OrdinalIgnoreCase);
        AcceptedSeriesName = trimmed;

        var cached = ExistingSeries.FirstOrDefault(s => s.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (cached is not null)
        {
            AcceptedSeriesId = cached.Id;
        }
        else
        {
            AcceptedSeriesId = await EagerEnsureSeriesAsync(trimmed);
            if (AcceptedSeriesId is int newId) ExistingSeries.Add(new SeriesOption(newId, trimmed));
        }

        if (changedSeries) AcceptedSeriesOrderLabel = null;
    }

    // Shared by the suggestion-Accept path and the manual field: eager find-or-
    // create a series by name → id (best-effort; the save's SeriesResolver net
    // still creates the row on a swallowed fault). Delegates to the shared
    // SeriesEagerCreate so BookAdd + BulkAdd carry one copy.
    private Task<int?> EagerEnsureSeriesAsync(string name)
        => SeriesEagerCreate.EnsureAsync(name, dispatcher, logger);

    // Existing-book detection — set during LookupAsync when the ISBN
    // already maps to an Edition in the library. The Add page surfaces a
    // banner offering "add another copy" / "edit existing" instead of
    // letting the user accidentally hit the unique-ISBN constraint by
    // saving a duplicate.
    public ExistingBookMatch? ExistingBook { get; private set; }
    public bool AddingCopy { get; private set; }

    /// <summary>True when the current <see cref="DispatchingViewModel.Error"/> came from the
    /// add-another-copy action, so the page shows it beside that button only.</summary>
    public bool AddCopyRefused { get; private set; }

    public async Task LookupAsync()
    {
        LookupMessage = null;
        ExistingBook = null;
        Error = null;
        AddCopyRefused = false;
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
            ExistingBook = await dispatcher.Query(new FindEditionByIsbn(cleanIsbn));
            if (ExistingBook is not null)
            {
                LookupMessage = null;
                return;
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
                ClearSeriesSelection();
            }
            else
            {
                LookupCandidates = [];
                SeriesSuggestion = null;
                SeriesSuggestionDismissed = false;
                ClearSeriesSelection();
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
            await DispatchAsync(() => dispatcher.Send(new AddCopyToEdition(
                ExistingBook.EditionId, CopyInput.Condition, DateAcquired: null, Notes: null)));
            AddCopyRefused = Error is not null;
            return Error is null ? ExistingBook.BookId : null;
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
        Error = null;
        AddCopyRefused = false;
        SeriesSuggestion = null;
        SeriesSuggestionDismissed = false;
        ClearSeriesSelection();
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
        ClearSeriesSelection();

        // No genre auto-pick for the no-ISBN flow — search results don't
        // carry subjects. User selects genres manually via the picker.
    }

    public async Task<int?> SaveAsync(List<int> selectedGenreIds)
    {
        // The page's EditForm validates its own model on submit, not these nested
        // inputs, so the cover URL rule is enforced here too and surfaces through
        // Error.
        Error = null;
        AddCopyRefused = false;
        if (!CoverFetchPolicy.IsAcceptableCoverUrl(BookInput.DefaultCoverArtUrl)
            || !CoverFetchPolicy.IsAcceptableCoverUrl(EditionInput.CoverUrl))
        {
            Error = CoverUrlAttribute.Message;
            return null;
        }

        Saving = true;
        try
        {
            // Collection mode sends every row (attach-existing and new) in
            // on-screen order with the shared-author / shared-genre modes; the
            // handler drops empty rows and owns the user-facing validation
            // messages. The chosen series (suggestion accept or manual
            // typeahead) lands on the Book in both modes: a collection is still
            // installment N of a publication series.
            var datePrinted = PartialDateParser.TryParse(EditionInput.DatePrinted) ?? PartialDate.Empty;
            var bookId = await DispatchAsync(() => dispatcher.Send(new CreateBook(
                new NewBook(
                    BookInput.Title ?? string.Empty,
                    BookInput.Category,
                    BookInput.Status,
                    BookInput.Rating,
                    BookInput.Notes,
                    BookInput.DefaultCoverArtUrl),
                IsCollection ? BuildCollection() : BuildSingleWork(selectedGenreIds),
                new NewEdition(
                    EditionInput.Isbn,
                    EditionInput.Format,
                    EditionInput.EditionNumber,
                    datePrinted.Date,
                    datePrinted.Precision,
                    EditionInput.Publisher,
                    EditionInput.CoverUrl),
                CopyInput.Condition,
                new SeriesChoice(AcceptedSeriesId, AcceptedSeriesName, AcceptedSeriesOrderLabel),
                [])));
            return Error is null ? bookId : null;
        }
        finally
        {
            Saving = false;
        }
    }

    // The single Work takes the page's genre picker selection; its title falls
    // back to the book title when the work title was never set.
    private SingleWork BuildSingleWork(List<int> selectedGenreIds)
    {
        var firstPub = PartialDateParser.TryParse(WorkInput.FirstPublishedDate) ?? PartialDate.Empty;
        return new SingleWork(
            WorkInput.Title ?? BookInput.Title ?? string.Empty,
            WorkInput.Subtitle,
            firstPub.Date,
            firstPub.Precision,
            WorkInput.Authors,
            ToContributorInputs(WorkInput.Contributors),
            selectedGenreIds);
    }

    private WorkCollection BuildCollection() => new(
        CollectionWorks.Select(r =>
        {
            var firstPub = PartialDateParser.TryParse(r.FirstPublishedDate) ?? PartialDate.Empty;
            return new WorkRow(
                r.AttachedWorkId,
                r.Title,
                r.Subtitle,
                firstPub.Date,
                firstPub.Precision,
                r.Authors,
                ToContributorInputs(r.Contributors),
                r.GenreIds);
        }).ToList(),
        SingleAuthor,
        SingleGenre,
        SharedAuthors,
        SharedGenreIds);

    private static List<ContributorInput> ToContributorInputs(List<ContributorEntry> entries) =>
        entries.Select(c => new ContributorInput(c.Name, c.Role)).ToList();
}
