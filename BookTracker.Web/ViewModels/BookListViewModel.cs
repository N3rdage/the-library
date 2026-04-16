using BookTracker.Data;
using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class BookListViewModel(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public const int PageSize = 20;

    public bool Loading { get; private set; } = true;
    public List<BookListItem> Books { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; }

    public string SearchTerm { get; set; } = "";
    public string SelectedCategory { get; set; } = "";
    public int SelectedGenreId { get; set; }
    public int SelectedTagId { get; set; }
    public string SelectedAuthor { get; set; } = "";

    public List<GenreOption> AllGenres { get; private set; } = [];
    public List<TagOption> AllTags { get; private set; } = [];
    public List<string> AllAuthors { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await LoadFilterOptionsAsync();
        await LoadBooksAsync();
    }

    private async Task LoadFilterOptionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var genres = await db.Genres
            .OrderBy(g => g.Name)
            .Select(g => new GenreOption(g.Id, g.Name, g.ParentGenreId))
            .ToListAsync();

        var topLevel = genres.Where(g => g.ParentGenreId is null).OrderBy(g => g.Name).ToList();
        AllGenres = [];
        foreach (var parent in topLevel)
        {
            AllGenres.Add(parent);
            var children = genres.Where(g => g.ParentGenreId == parent.Id).OrderBy(g => g.Name);
            AllGenres.AddRange(children);
        }

        AllTags = await db.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagOption(t.Id, t.Name))
            .ToListAsync();

        AllAuthors = await db.Books
            .Select(b => b.Author)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();
    }

    public async Task LoadBooksAsync()
    {
        Loading = true;

        await using var db = await dbFactory.CreateDbContextAsync();

        IQueryable<Book> query = db.Books
            .Include(b => b.Genres)
            .Include(b => b.Tags);

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.Trim();
            query = query.Where(b => b.Title.Contains(term) || b.Author.Contains(term));
        }

        if (!string.IsNullOrEmpty(SelectedCategory) && Enum.TryParse<BookCategory>(SelectedCategory, out var cat))
        {
            query = query.Where(b => b.Category == cat);
        }

        if (SelectedGenreId > 0)
        {
            query = query.Where(b => b.Genres.Any(g => g.Id == SelectedGenreId));
        }

        if (SelectedTagId > 0)
        {
            query = query.Where(b => b.Tags.Any(t => t.Id == SelectedTagId));
        }

        if (!string.IsNullOrWhiteSpace(SelectedAuthor))
        {
            var author = SelectedAuthor.Trim();
            query = query.Where(b => b.Author == author);
        }

        TotalCount = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;

        Books = await query
            .OrderByDescending(b => b.DateAdded)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(b => new BookListItem(
                b.Id,
                b.Title,
                b.Subtitle,
                b.Author,
                b.DefaultCoverArtUrl,
                b.Status,
                b.Rating,
                b.Genres.Select(g => g.Name).ToList(),
                b.Tags.Select(t => t.Name).ToList()
            ))
            .ToListAsync();

        Loading = false;
    }

    public async Task ApplyFiltersAsync()
    {
        CurrentPage = 1;
        await LoadBooksAsync();
    }

    public async Task ClearFiltersAsync()
    {
        SearchTerm = "";
        SelectedCategory = "";
        SelectedGenreId = 0;
        SelectedTagId = 0;
        SelectedAuthor = "";
        CurrentPage = 1;
        await LoadBooksAsync();
    }

    public async Task GoToPageAsync(int page)
    {
        if (page < 1 || page > TotalPages) return;
        CurrentPage = page;
        await LoadBooksAsync();
    }

    public static string StatusBadgeClass(BookStatus status) => status switch
    {
        BookStatus.Reading => "bg-primary",
        BookStatus.Read => "bg-success",
        BookStatus.Unread => "bg-secondary",
        _ => "bg-secondary"
    };

    public record BookListItem(
        int Id, string Title, string? Subtitle, string Author, string? CoverUrl,
        BookStatus Status, int Rating, List<string> Genres, List<string> Tags);

    public record GenreOption(int Id, string Name, int? ParentGenreId);
    public record TagOption(int Id, string Name);
}
