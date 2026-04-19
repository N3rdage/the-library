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

        if (Rows.Any(r => string.Equals(r.Isbn, isbn, StringComparison.OrdinalIgnoreCase)))
        {
            IsbnInput = "";
            return;
        }

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
                row.GenreCandidates = result.GenreCandidates.ToList();
                row.Format = result.Format;
                row.Status = RowStatus.Found;

                row.SeriesSuggestion = await seriesMatch.FindMatchAsync(result.Title, result.Author);
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

        if (row.IsDuplicate)
        {
            var existingEdition = await db.Editions
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

        var newBook = new Book
        {
            Title = (row.Title ?? $"Unknown book — {row.Isbn}").Trim(),
            Subtitle = row.Subtitle,
            Author = (row.Author ?? "Unknown").Trim(),
            DefaultCoverArtUrl = row.CoverUrl,
            Editions =
            [
                new Edition
                {
                    Isbn = row.Isbn,
                    Format = row.Format ?? BookFormat.TradePaperback,
                    DatePrinted = row.DatePrinted,
                    Publisher = publisher,
                    Copies = [new Copy { Condition = BookCondition.Good }]
                }
            ]
        };

        if (row.GenreCandidates.Count > 0)
        {
            var allGenres = await db.Genres.ToListAsync();
            var matched = new HashSet<int>();
            foreach (var candidate in row.GenreCandidates)
            {
                var genre = allGenres.FirstOrDefault(g => GenrePickerViewModel.FuzzyGenreMatch(candidate, g.Name));
                if (genre is not null && matched.Add(genre.Id))
                {
                    newBook.Genres.Add(genre);
                    if (genre.ParentGenreId is int parentId && matched.Add(parentId))
                    {
                        var parent = allGenres.FirstOrDefault(g => g.Id == parentId);
                        if (parent is not null) newBook.Genres.Add(parent);
                    }
                }
            }
        }

        db.Books.Add(newBook);
        WorkSync.EnsureWork(newBook);
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
        public List<string> GenreCandidates { get; set; } = [];
        // Null when the lookup couldn't infer a confident format; the save
        // path falls back to TradePaperback so manual override still wins
        // pre-save.
        public BookFormat? Format { get; set; }
        public RowStatus Status { get; set; }
        public RowAction Action { get; set; } = RowAction.Pending;
        public bool IsDuplicate { get; set; }
        public SeriesMatch? SeriesSuggestion { get; set; }
    }

    public enum RowStatus { Searching, Found, NotFound }
    public enum RowAction { Pending, Accepted, FollowUp, Duplicate }
}
