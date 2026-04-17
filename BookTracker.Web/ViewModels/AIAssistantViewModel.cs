using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

public class AIAssistantViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IAIAssistantService aiService)
{
    // Genre cleanup
    public List<BookGenreRow> BooksNeedingGenres { get; private set; } = [];
    public bool GenresLoaded { get; private set; }
    public int? SuggestingForBookId { get; private set; }
    public int? SuggestedBookId { get; private set; }
    public GenreSuggestionResult? CurrentSuggestion { get; private set; }
    public string? GenreError { get; private set; }

    public int CallCount => aiService.CallCount;

    public async Task LoadBooksNeedingGenresAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        BooksNeedingGenres = await db.Books
            .Include(b => b.Genres)
            .OrderBy(b => b.Genres.Count)
            .ThenBy(b => b.Title)
            .Take(50)
            .Select(b => new BookGenreRow(
                b.Id,
                b.Title,
                b.Subtitle,
                b.Author,
                b.Genres.Select(g => g.Name).ToList()))
            .ToListAsync();

        GenresLoaded = true;
    }

    public async Task SuggestGenresForBookAsync(int bookId)
    {
        var book = BooksNeedingGenres.FirstOrDefault(b => b.Id == bookId);
        if (book is null) return;

        SuggestingForBookId = bookId;
        SuggestedBookId = bookId;
        CurrentSuggestion = null;
        GenreError = null;

        try
        {
            CurrentSuggestion = await aiService.SuggestGenresAsync(
                book.Title, book.Author, book.Subtitle, book.CurrentGenres);
        }
        catch (Exception ex)
        {
            GenreError = $"AI request failed: {ex.Message}";
        }
        finally
        {
            SuggestingForBookId = null;
        }
    }

    public async Task AcceptGenreSuggestionsAsync(int bookId, IReadOnlyList<string> genreNames)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var book = await db.Books.Include(b => b.Genres).FirstOrDefaultAsync(b => b.Id == bookId);
        if (book is null) return;

        var genresToAdd = await db.Genres
            .Where(g => genreNames.Contains(g.Name))
            .ToListAsync();

        foreach (var genre in genresToAdd)
        {
            if (!book.Genres.Any(g => g.Id == genre.Id))
                book.Genres.Add(genre);
        }

        await db.SaveChangesAsync();

        // Update the local list
        var row = BooksNeedingGenres.FirstOrDefault(b => b.Id == bookId);
        if (row is not null)
        {
            var idx = BooksNeedingGenres.IndexOf(row);
            var updatedGenres = book.Genres.Select(g => g.Name).ToList();
            BooksNeedingGenres[idx] = row with { CurrentGenres = updatedGenres };
        }

        CurrentSuggestion = null;
    }

    public void DismissSuggestion()
    {
        CurrentSuggestion = null;
        SuggestedBookId = null;
        GenreError = null;
    }

    public record BookGenreRow(
        int Id, string Title, string? Subtitle, string Author,
        List<string> CurrentGenres);
}
