using System.ComponentModel.DataAnnotations;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.Pages.Books;

public class AddModel(BookTrackerDbContext db, IBookLookupService lookup) : PageModel
{
    [BindProperty]
    public AddBookInput Input { get; set; } = new();

    public List<GenreOption> ExistingGenres { get; private set; } = [];

    public string? LookupMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadExistingGenresAsync(ct);
    }

    public async Task<IActionResult> OnPostLookupAsync(CancellationToken ct)
    {
        ModelState.Clear();

        if (string.IsNullOrWhiteSpace(Input.LookupIsbn))
        {
            LookupMessage = "Enter an ISBN to look up.";
            await LoadExistingGenresAsync(ct);
            return Page();
        }

        var result = await lookup.LookupByIsbnAsync(Input.LookupIsbn, ct);
        if (result is null)
        {
            LookupMessage = $"No match found for ISBN {Input.LookupIsbn}.";
            await LoadExistingGenresAsync(ct);
            return Page();
        }

        Input.Title ??= "";
        if (string.IsNullOrWhiteSpace(Input.Title)) Input.Title = result.Title ?? "";
        if (string.IsNullOrWhiteSpace(Input.Author)) Input.Author = result.Author ?? "";
        if (string.IsNullOrWhiteSpace(Input.DefaultCoverArtUrl)) Input.DefaultCoverArtUrl = result.CoverUrl;
        if (string.IsNullOrWhiteSpace(Input.Isbn)) Input.Isbn = result.Isbn;
        Input.DatePrinted ??= result.DatePrinted;

        await LoadExistingGenresAsync(ct);

        var existingNames = ExistingGenres.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toTick = new HashSet<int>(Input.SelectedGenreIds);
        var extras = new List<string>();
        foreach (var candidate in result.GenreCandidates)
        {
            var match = ExistingGenres.FirstOrDefault(g => string.Equals(g.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                toTick.Add(match.Id);
            }
            else if (!existingNames.Contains(candidate))
            {
                extras.Add(candidate);
            }
        }
        Input.SelectedGenreIds = [.. toTick];

        var merged = (Input.NewGenres ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(extras)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        Input.NewGenres = string.Join(", ", merged);

        LookupMessage = $"Prefilled from {result.Source}. Edit anything before saving.";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadExistingGenresAsync(ct);
            return Page();
        }

        var selectedGenres = await db.Genres
            .Where(g => Input.SelectedGenreIds.Contains(g.Id))
            .ToListAsync(ct);

        var newGenreNames = (Input.NewGenres ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in newGenreNames)
        {
            var existing = await db.Genres.FirstOrDefaultAsync(g => g.Name == name, ct);
            if (existing is not null)
            {
                if (!selectedGenres.Any(g => g.Id == existing.Id))
                {
                    selectedGenres.Add(existing);
                }
            }
            else
            {
                var created = new Genre { Name = name };
                db.Genres.Add(created);
                selectedGenres.Add(created);
            }
        }

        var book = new Book
        {
            Title = Input.Title!.Trim(),
            Author = Input.Author!.Trim(),
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            Status = Input.Status,
            Rating = Input.Rating,
            DefaultCoverArtUrl = string.IsNullOrWhiteSpace(Input.DefaultCoverArtUrl) ? null : Input.DefaultCoverArtUrl.Trim(),
            Genres = selectedGenres,
            Copies =
            [
                new BookCopy
                {
                    Isbn = Input.Isbn!.Trim(),
                    Format = Input.Format,
                    DatePrinted = Input.DatePrinted,
                    Condition = Input.Condition,
                    CustomCoverArtUrl = string.IsNullOrWhiteSpace(Input.CustomCoverArtUrl) ? null : Input.CustomCoverArtUrl.Trim()
                }
            ]
        };

        db.Books.Add(book);
        await db.SaveChangesAsync(ct);

        return RedirectToPage("/Index");
    }

    private async Task LoadExistingGenresAsync(CancellationToken ct)
    {
        ExistingGenres = await db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new GenreOption(g.Id, g.Name))
            .ToListAsync(ct);
    }

    public record GenreOption(int Id, string Name);

    public class AddBookInput
    {
        public string? LookupIsbn { get; set; }

        [Required, StringLength(300)]
        public string? Title { get; set; }

        [Required, StringLength(200)]
        public string? Author { get; set; }

        public BookStatus Status { get; set; } = BookStatus.Unread;

        [Range(0, 5)]
        public int Rating { get; set; }

        public string? Notes { get; set; }

        [StringLength(500)]
        public string? DefaultCoverArtUrl { get; set; }

        public List<int> SelectedGenreIds { get; set; } = [];

        public string? NewGenres { get; set; }

        [Required, StringLength(20)]
        [RegularExpression(@"^(97(8|9))?\d{9}(\d|X|x)$", ErrorMessage = "Enter a valid 10- or 13-digit ISBN.")]
        public string? Isbn { get; set; }

        public BookFormat Format { get; set; } = BookFormat.Hardcopy;

        public DateOnly? DatePrinted { get; set; }

        public BookCondition Condition { get; set; } = BookCondition.Good;

        [StringLength(500)]
        public string? CustomCoverArtUrl { get; set; }
    }

    public IEnumerable<SelectListItem> StatusOptions => Enum.GetValues<BookStatus>()
        .Select(s => new SelectListItem(s.ToString(), s.ToString()));

    public IEnumerable<SelectListItem> FormatOptions => Enum.GetValues<BookFormat>()
        .Select(f => new SelectListItem(f.ToString(), f.ToString()));

    public IEnumerable<SelectListItem> ConditionOptions => Enum.GetValues<BookCondition>()
        .Select(c => new SelectListItem(FormatCondition(c), c.ToString()));

    private static string FormatCondition(BookCondition c) => c switch
    {
        BookCondition.AsNew => "As New",
        BookCondition.VeryGood => "Very Good",
        _ => c.ToString()
    };
}
