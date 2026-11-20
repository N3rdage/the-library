using BookTracker.Application;
using BookTracker.Application.Authors;
using BookTracker.Application.Books;
using BookTracker.Application.Formatting;
using BookTracker.Application.Genres;
using BookTracker.Application.Series;
using BookTracker.Data.Models;
using BookTracker.Web.Components.Shared;
using BookTracker.Web.Services;
using Microsoft.Extensions.Logging;

namespace BookTracker.Web.ViewModels;

public class BulkAddViewModel(
    IBookLookupService lookup,
    SeriesMatchService seriesMatch,
    AIProviderFactory providerFactory,
    ILogger<BulkAddViewModel> logger,
    IDispatcher dispatcher) : DispatchingViewModel
{
    /// <summary>
    /// Callback for the component to marshal state changes back to the UI thread.
    /// Wire this to <c>() => InvokeAsync(StateHasChanged)</c> in the component.
    /// </summary>
    public Func<Task>? OnStateChanged { get; set; }

    /// <summary>
    /// The page's "Ask AI for genres" switch. Off by default and per circuit,
    /// never persisted. With it on, a row whose rule-based genre match finds
    /// nothing or only top-level genres asks the active provider as part of
    /// its lookup; with it off the lookup makes no AI call.
    /// </summary>
    public bool AskAiForGenres { get; set; }

    public string IsbnInput { get; set; } = "";
    public List<DiscoveryRow> Rows { get; } = [];

    public void RemoveRow(DiscoveryRow row) => Rows.Remove(row);

    public async Task AddIsbnAsync()
    {
        var isbn = IsbnInput.Trim();
        if (string.IsNullOrWhiteSpace(isbn)) return;

        // Re-scanning the same ISBN is allowed and meaningful: each row
        // represents one physical book to add, so a second scan adds a
        // second copy. TryHydrateFromExistingAsync flags the row as a
        // duplicate (against the DB) and populates display fields from
        // the local Edition so the user sees what they own — upstream
        // lookup is skipped in that case because the providers may not
        // index the ISBN even though it's already in the library, which
        // would otherwise leave the row reading "Unknown book".
        var row = new DiscoveryRow { Isbn = isbn, Status = RowStatus.Searching };
        Rows.Insert(0, row);
        IsbnInput = "";

        if (await TryHydrateFromExistingAsync(row)) return;

        _ = LookupRowAsync(row);
    }

    private async Task<bool> TryHydrateFromExistingAsync(DiscoveryRow row)
    {
        var existing = await dispatcher.Query(new FindEditionByIsbn(row.Isbn));
        if (existing is null) return false;

        // Single-Work books are the common case; compendiums fall back to
        // the lead Work for subtitle/author display. Save path doesn't care
        // (it appends a Copy to the existing Edition either way).
        row.IsDuplicate = true;
        row.Title = existing.Title;
        row.Subtitle = existing.LeadWorkSubtitle;
        row.Author = existing.LeadWorkAuthorship;
        row.CoverUrl = existing.CoverUrl;
        row.Source = "Local library";
        row.Publisher = existing.Publisher;
        row.DatePrinted = existing.DatePrinted;
        row.DatePrintedPrecision = existing.DatePrintedPrecision;
        row.Format = existing.Format;
        row.Status = RowStatus.Found;
        return true;
    }

    private async Task LookupRowAsync(DiscoveryRow row)
    {
        try
        {
            var result = await lookup.LookupByIsbnAsync(row.Isbn, CancellationToken.None);
            if (result is not null)
            {
                row.Title = result.Title;
                row.Author = result.Author;
                row.CoverUrl = result.CoverUrl;
                row.Source = result.Source;
                row.Publisher = result.Publisher;
                row.Subtitle = result.Subtitle;
                row.DatePrinted = result.DatePrinted;
                row.DatePrintedPrecision = result.DatePrintedPrecision;
                row.Format = result.Format;
                // Genres before Found: the grid offers Accept and "Accept all
                // found" selects on Found, so a row that turned Found first
                // could be saved with empty genres while the taxonomy query
                // or the AI call was still pending.
                row.Genres = await ResolveGenresAsync(row, result.GenreCandidates);
                row.Status = RowStatus.Found;

                // SeriesMatch is best-effort enrichment — failure here must not
                // flip the row back to NotFound, so it's wrapped separately.
                try
                {
                    row.SeriesSuggestion = await seriesMatch.FindMatchAsync(result);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Series match failed for ISBN {Isbn} — row left without suggestion", row.Isbn);
                }
            }
            else
            {
                row.Title = $"Unknown book — {row.Isbn}";
                row.Status = RowStatus.NotFound;
            }
        }
        catch (Exception ex)
        {
            // Silent catch was masking real failure modes (disposed HttpClient,
            // upstream timeout, EF translation regression). Log so the next
            // failure has a trace to follow.
            logger.LogError(ex, "Bulk-add lookup failed for ISBN {Isbn}", row.Isbn);
            row.Title = $"Unknown book — {row.Isbn}";
            row.Status = RowStatus.NotFound;
        }

        if (OnStateChanged is not null)
            await OnStateChanged();
    }

    // A row leaves the grid only once its save succeeded; a refused save keeps
    // the row and sets Error.
    public async Task<bool> AcceptRowAsync(DiscoveryRow row)
    {
        if (!await SaveBookAsync(row, followUp: false)) return false;
        Rows.Remove(row);
        return true;
    }

    public async Task FollowUpRowAsync(DiscoveryRow row)
    {
        if (await SaveBookAsync(row, followUp: true)) Rows.Remove(row);
    }

    public async Task FollowUpNotFoundAsync(DiscoveryRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Title))
        {
            row.Title = $"Unknown book — {row.Isbn}";
        }
        if (await SaveBookAsync(row, followUp: true)) Rows.Remove(row);
    }

    public async Task AcceptAllFoundAsync()
    {
        var pending = Rows.Where(r => r.Status == RowStatus.Found && r.Action == RowAction.Pending && !r.IsDuplicate).ToList();
        foreach (var row in pending)
        {
            if (!await AcceptRowAsync(row)) return;
        }
    }

    private async Task<bool> SaveBookAsync(DiscoveryRow row, bool followUp)
    {
        IReadOnlyList<string> tagNames = followUp ? [FollowUpTag] : [];

        // Re-check at save time rather than relying on row.IsDuplicate (which
        // was set when the row was scanned). Two scans of the same ISBN in
        // one session both start as not-duplicate, but by the time the
        // second one's saved the first row may have already been accepted
        // and inserted the Edition — in which case we want to add a Copy,
        // not crash on the unique-ISBN constraint.
        if (!string.IsNullOrWhiteSpace(row.Isbn))
        {
            var addedCopy = await DispatchAsync(() => dispatcher.Send(new AddScannedCopy(row.Isbn, tagNames)));
            if (Error is not null) return false;
            if (addedCopy is not null) return true;
        }

        var bookTitle = (row.Title ?? $"Unknown book — {row.Isbn}").Trim();
        // row.Author can be a comma- or ampersand-separated list of names from
        // the inline cell (Drew typing "Preston, Lincoln Child"). ParseNames
        // splits + trims; the fallback to "Unknown" matches the prior single-
        // author behaviour for not-found / blank rows.
        var authorNames = AuthorResolver.ParseNames(row.Author);
        if (authorNames.Count == 0) authorNames = ["Unknown"];

        await DispatchAsync(() => dispatcher.Send(new CreateBook(
            new NewBook(bookTitle, BookCategory.Fiction, BookStatus.Unread, 0, null, row.CoverUrl),
            new SingleWork(bookTitle, row.Subtitle, null, DatePrecision.Day, authorNames, [], row.Genres.Select(g => g.Id).ToList()),
            new NewEdition(
                row.Isbn,
                row.Format ?? BookFormat.TradePaperback,
                null,
                row.DatePrinted,
                row.DatePrintedPrecision,
                row.Publisher,
                null),
            BookCondition.Good,
            // Series attachment only if the user accepted the suggestion on this
            // row. Series membership is a per-Book concept (the book is
            // installment N).
            row.SeriesSuggestionAccepted
                ? new SeriesChoice(row.AcceptedSeriesId, row.AcceptedSeriesName, row.AcceptedSeriesOrderLabel)
                : new SeriesChoice(null, null, null),
            tagNames)));
        return Error is null;
    }

    /// <summary>
    /// Resolves a row's genres once its lookup has returned, so the grid shows
    /// them before Accept. Each lookup candidate picks the first genre (in id
    /// order) it fuzzy-matches, plus that genre's parent; each genre is picked
    /// once. Then, with <see cref="AskAiForGenres"/> on and that match empty
    /// or top-level only, the active provider's suggestions are added by the
    /// same rule, keeping only names in the taxonomy and marking them AI-sourced.
    /// </summary>
    private async Task<List<RowGenre>> ResolveGenresAsync(DiscoveryRow row, IReadOnlyList<string> candidates)
    {
        var genres = new List<RowGenre>();
        // No candidates to match and nothing the AI could be asked about (switch
        // off, or no title): the taxonomy query would feed nothing.
        if (candidates.Count == 0 && (!AskAiForGenres || string.IsNullOrWhiteSpace(row.Title))) return genres;

        var allGenres = (await dispatcher.Query(new GetGenreOptions())).OrderBy(g => g.Id).ToList();
        foreach (var candidate in candidates)
        {
            AddWithParent(genres, allGenres, allGenres.FirstOrDefault(g => MudGenrePickerViewModel.FuzzyGenreMatch(candidate, g.Name)), fromAi: false);
        }

        // A matched child already places the book precisely; a bare top-level
        // genre (or none) is where the subject strings ran out and the AI earns
        // its call. Nothing to classify without a title.
        var hasChild = genres.Any(g => allGenres.Any(o => o.Id == g.Id && o.ParentGenreId is not null));
        if (!AskAiForGenres || hasChild || string.IsNullOrWhiteSpace(row.Title)) return genres;

        // Best-effort like the series match: a failing or slow provider leaves
        // the rule-based genres in place and the row still completes.
        try
        {
            var suggestion = await providerFactory.GetService().SuggestGenresAsync(
                row.Title, row.Author ?? string.Empty, row.Subtitle, genres.Select(g => g.Name).ToList());
            foreach (var name in suggestion.SuggestedGenres)
            {
                AddWithParent(genres, allGenres, allGenres.FirstOrDefault(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)), fromAi: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI genre suggestion failed for ISBN {Isbn} — row keeps its rule-based genres", row.Isbn);
        }
        return genres;
    }

    private static void AddWithParent(List<RowGenre> genres, List<GenrePickerOption> allGenres, GenrePickerOption? genre, bool fromAi)
    {
        if (genre is null || genres.Any(g => g.Id == genre.Id)) return;

        genres.Add(new RowGenre(genre.Id, genre.Name, fromAi));
        if (genre.ParentGenreId is int parentId && !genres.Any(g => g.Id == parentId)
            && allGenres.FirstOrDefault(g => g.Id == parentId) is { } parent)
        {
            genres.Add(new RowGenre(parent.Id, parent.Name, fromAi));
        }
    }

    private const string FollowUpTag = "follow-up";

    public static string RowCssClass(DiscoveryRow row) => row.Action switch
    {
        RowAction.Accepted => "table-success",
        RowAction.FollowUp => "table-warning",
        RowAction.Duplicate => "table-secondary",
        _ => ""
    };

    public class DiscoveryRow
    {
        public string Isbn { get; set; } = "";
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? Author { get; set; }
        public string? CoverUrl { get; set; }
        public string? Source { get; set; }
        public string? Publisher { get; set; }
        public DateOnly? DatePrinted { get; set; }
        public DatePrecision DatePrintedPrecision { get; set; } = DatePrecision.Day;
        // Resolved against the taxonomy when the lookup returns (rule-based
        // first, then the AI's when asked) so the grid shows them before
        // Accept; the save sends exactly these ids.
        public List<RowGenre> Genres { get; set; } = [];
        // Null when the lookup couldn't infer a confident format; the save
        // path falls back to TradePaperback so manual override still wins
        // pre-save.
        public BookFormat? Format { get; set; }
        public RowStatus Status { get; set; }
        public RowAction Action { get; set; } = RowAction.Pending;
        public bool IsDuplicate { get; set; }
        public SeriesMatch? SeriesSuggestion { get; set; }
        // Per-row acceptance state for the series suggestion banner. When the
        // user clicks Accept on this row, we capture the suggestion's identity
        // here; SaveBookAsync reads it and attaches the Work to the right
        // Series row (find-or-create for ApiMatchNewSeries).
        public bool SeriesSuggestionAccepted { get; set; }
        public int? AcceptedSeriesId { get; set; }
        public string? AcceptedSeriesName { get; set; }
        // Single round-trippable order label ("5" / "4.5") captured at accept
        // time; re-parsed into (SeriesOrder, SeriesOrderDisplay) at save.
        public string? AcceptedSeriesOrderLabel { get; set; }
    }

    /// <summary>One genre on a row. <paramref name="FromAi"/> marks a name the
    /// active provider suggested (or the parent it brought in) rather than one
    /// the rule-based match found, so the grid can flag it before Accept.</summary>
    public record RowGenre(int Id, string Name, bool FromAi);

    public async Task AcceptSeriesSuggestion(DiscoveryRow row)
    {
        if (row.SeriesSuggestion is null) return;
        // Only API-sourced suggestions (Existing / NewSeries) are actionable —
        // mirrors BookAddViewModel.AcceptSeriesSuggestion.
        if (row.SeriesSuggestion.Reason is not (MatchReason.ApiMatchExisting or MatchReason.ApiMatchNewSeries))
        {
            return;
        }
        row.AcceptedSeriesId = row.SeriesSuggestion.SeriesId;
        row.AcceptedSeriesName = row.SeriesSuggestion.SeriesName;
        row.AcceptedSeriesOrderLabel = SeriesOrderParser.Format(row.SeriesSuggestion.SuggestedOrder, row.SeriesSuggestion.SuggestedOrderDisplay);
        row.SeriesSuggestionAccepted = true;

        // Accept is the commit gesture (a discrete per-row button click), so
        // eager-create a genuinely-new series now (TD-15a) and pin its id — the
        // save then attaches by id instead of find-or-creating. Best-effort (shared
        // SeriesEagerCreate): on failure the id stays null and the save's
        // SeriesResolver net still creates it by name.
        if (row.AcceptedSeriesId is null && !string.IsNullOrWhiteSpace(row.AcceptedSeriesName))
        {
            row.AcceptedSeriesId = await SeriesEagerCreate.EnsureAsync(row.AcceptedSeriesName, dispatcher, logger);
        }
    }

    public void UndoSeriesSuggestionAccept(DiscoveryRow row)
    {
        row.SeriesSuggestionAccepted = false;
        row.AcceptedSeriesId = null;
        row.AcceptedSeriesName = null;
        row.AcceptedSeriesOrderLabel = null;
    }

    public enum RowStatus { Searching, Found, NotFound }
    public enum RowAction { Pending, Accepted, FollowUp, Duplicate }
}
