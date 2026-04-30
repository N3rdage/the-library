using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class BulkAddViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup,
    SeriesMatchService seriesMatch)
{
    /// <summary>
    /// Callback for the component to marshal state changes back to the UI thread.
    /// Wire this to <c>() => InvokeAsync(StateHasChanged)</c> in the component.
    /// </summary>
    public Func<Task>? OnStateChanged { get; set; }

    public string IsbnInput { get; set; } = "";
    public List<DiscoveryRow> Rows { get; } = [];

    public void RemoveRow(DiscoveryRow row) => Rows.Remove(row);

    public async Task AddIsbnAsync()
    {
        var isbn = IsbnInput.Trim();
        if (string.IsNullOrWhiteSpace(isbn)) return;

        // Re-scanning the same ISBN is allowed and meaningful: each row
        // represents one physical book to add, so a second scan adds a
        // second copy. CheckDuplicateAsync flags the row as a duplicate
        // (either against the DB or a previous in-session row) and the
        // save path appends a new Copy to the existing Edition rather
        // than trying to create a colliding Book.
        var row = new DiscoveryRow { Isbn = isbn, Status = RowStatus.Searching };
        Rows.Insert(0, row);
        IsbnInput = "";

        await CheckDuplicateAsync(row);

        _ = LookupRowAsync(row);
    }

    private async Task CheckDuplicateAsync(DiscoveryRow row)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        row.IsDuplicate = await db.Editions.AnyAsync(e => e.Isbn == row.Isbn);
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
                row.GenreCandidates = result.GenreCandidates.ToList();
                row.Format = result.Format;
                row.Status = RowStatus.Found;

                row.SeriesSuggestion = await seriesMatch.FindMatchAsync(result);
            }
            else
            {
                row.Title = $"Unknown book — {row.Isbn}";
                row.Status = RowStatus.NotFound;
            }
        }
        catch
        {
            row.Title = $"Unknown book — {row.Isbn}";
            row.Status = RowStatus.NotFound;
        }

        if (OnStateChanged is not null)
            await OnStateChanged();
    }

    public async Task AcceptRowAsync(DiscoveryRow row)
    {
        await SaveBookAsync(row, followUp: false);
        row.Action = RowAction.Accepted;
    }

    public async Task FollowUpRowAsync(DiscoveryRow row)
    {
        await SaveBookAsync(row, followUp: true);
        row.Action = RowAction.FollowUp;
    }

    public async Task FollowUpNotFoundAsync(DiscoveryRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Title))
        {
            row.Title = $"Unknown book — {row.Isbn}";
        }
        await SaveBookAsync(row, followUp: true);
        row.Action = RowAction.FollowUp;
    }

    public async Task AcceptAllFoundAsync()
    {
        var pending = Rows.Where(r => r.Status == RowStatus.Found && r.Action == RowAction.Pending && !r.IsDuplicate).ToList();
        foreach (var row in pending)
        {
            await AcceptRowAsync(row);
        }
    }

    private async Task SaveBookAsync(DiscoveryRow row, bool followUp)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Re-check at save time rather than relying on row.IsDuplicate (which
        // was set when the row was scanned). Two scans of the same ISBN in
        // one session both start as not-duplicate, but by the time the
        // second one's saved the first row may have already been accepted
        // and inserted the Edition — in which case we want to add a Copy,
        // not crash on the unique-ISBN constraint.
        var existingEdition = string.IsNullOrWhiteSpace(row.Isbn)
            ? null
            : await db.Editions
                .Include(e => e.Book)
                .FirstOrDefaultAsync(e => e.Isbn == row.Isbn);

        if (existingEdition is not null)
        {
            var newCopy = new Copy
            {
                EditionId = existingEdition.Id,
                Condition = BookCondition.Good
            };
            db.Copies.Add(newCopy);

            if (followUp)
            {
                var book = await db.Books.Include(b => b.Tags).FirstAsync(b => b.Id == existingEdition.BookId);
                await EnsureFollowUpTagAsync(db, book);
            }

            await db.SaveChangesAsync();
            return;
        }

        Publisher? publisher = null;
        var pubName = row.Publisher?.Trim();
        if (!string.IsNullOrEmpty(pubName))
        {
            publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);
            if (publisher is null)
            {
                publisher = new Publisher { Name = pubName };
                db.Publishers.Add(publisher);
            }
        }

        var bookTitle = (row.Title ?? $"Unknown book — {row.Isbn}").Trim();
        var author = await AuthorResolver.FindOrCreateAsync(row.Author ?? "Unknown", db);
        var work = new Work
        {
            Title = bookTitle,
            Subtitle = row.Subtitle,
            Author = author,
        };

        // Series attachment if the user accepted the suggestion on this row.
        // Mirrors BookAddViewModel.SaveAsync: existing local series wins via
        // SeriesId; otherwise find-or-create by name and attach the new row.
        if (row.SeriesSuggestionAccepted)
        {
            if (row.AcceptedSeriesId is int existingId)
            {
                work.SeriesId = existingId;
                work.SeriesOrder = row.AcceptedSeriesOrder;
            }
            else if (!string.IsNullOrWhiteSpace(row.AcceptedSeriesName))
            {
                var seriesName = row.AcceptedSeriesName.Trim();
                var series = await db.Series
                    .FirstOrDefaultAsync(s => s.Name.ToLower() == seriesName.ToLower());
                if (series is null)
                {
                    series = new Series { Name = seriesName, Type = SeriesType.Series };
                    db.Series.Add(series);
                }
                work.Series = series;
                work.SeriesOrder = row.AcceptedSeriesOrder;
            }
        }

        if (row.GenreCandidates.Count > 0)
        {
            var allGenres = await db.Genres.ToListAsync();
            var matched = new HashSet<int>();
            foreach (var candidate in row.GenreCandidates)
            {
                var genre = allGenres.FirstOrDefault(g => GenrePickerViewModel.FuzzyGenreMatch(candidate, g.Name));
                if (genre is not null && matched.Add(genre.Id))
                {
                    work.Genres.Add(genre);
                    if (genre.ParentGenreId is int parentId && matched.Add(parentId))
                    {
                        var parent = allGenres.FirstOrDefault(g => g.Id == parentId);
                        if (parent is not null) work.Genres.Add(parent);
                    }
                }
            }
        }

        var newBook = new Book
        {
            Title = bookTitle,
            DefaultCoverArtUrl = row.CoverUrl,
            Works = [work],
            Editions =
            [
                new Edition
                {
                    Isbn = row.Isbn,
                    Format = row.Format ?? BookFormat.TradePaperback,
                    DatePrinted = row.DatePrinted,
                    DatePrintedPrecision = row.DatePrintedPrecision,
                    Publisher = publisher,
                    Copies = [new Copy { Condition = BookCondition.Good }]
                }
            ]
        };

        db.Books.Add(newBook);
        await db.SaveChangesAsync();

        if (followUp)
        {
            await EnsureFollowUpTagAsync(db, newBook);
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureFollowUpTagAsync(BookTrackerDbContext db, Book book)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == "follow-up");
        if (tag is null)
        {
            tag = new Tag { Name = "follow-up" };
            db.Tags.Add(tag);
        }
        if (!book.Tags.Any(t => t.Name == "follow-up"))
        {
            book.Tags.Add(tag);
        }
    }

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
        public List<string> GenreCandidates { get; set; } = [];
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
        public int? AcceptedSeriesOrder { get; set; }
    }

    public void AcceptSeriesSuggestion(DiscoveryRow row)
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
        row.AcceptedSeriesOrder = row.SeriesSuggestion.SuggestedOrder;
        row.SeriesSuggestionAccepted = true;
    }

    public void UndoSeriesSuggestionAccept(DiscoveryRow row)
    {
        row.SeriesSuggestionAccepted = false;
        row.AcceptedSeriesId = null;
        row.AcceptedSeriesName = null;
        row.AcceptedSeriesOrder = null;
    }

    public enum RowStatus { Searching, Found, NotFound }
    public enum RowAction { Pending, Accepted, FollowUp, Duplicate }
}
