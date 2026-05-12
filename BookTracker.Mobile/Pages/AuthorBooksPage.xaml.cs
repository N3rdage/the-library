using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

public partial class AuthorBooksPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly AuthorSnapshot _author;
    private bool _loaded;

    private static readonly Color Brass = Color.FromArgb("#A67B3A");
    private static readonly Color AgedParchment = Color.FromArgb("#F2EADB");
    private static readonly Color Ink = Color.FromArgb("#2C2416");
    private static readonly Color FadedInk = Color.FromArgb("#6B5D4A");
    private static readonly Color ErrorColor = Color.FromArgb("#9B3B2E");

    public AuthorBooksPage(ICatalogCache cache, AuthorSnapshot author)
    {
        InitializeComponent();
        _cache = cache;
        _author = author;
        // Title shows in the system nav bar — sets the breadcrumb the
        // user uses to back out of the result list.
        Title = $"Books by {author.Name}";
        HeaderLabel.Text = author.BookCount == 1
            ? "1 book in your library"
            : $"{author.BookCount:N0} books in your library";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await LoadBooksAsync();
    }

    private async Task LoadBooksAsync()
    {
        BooksLayout.Children.Clear();
        BooksLayout.Children.Add(new ActivityIndicator
        {
            IsRunning = true,
            Color = Color.FromArgb("#6B2737"),
            HorizontalOptions = LayoutOptions.Center,
        });

        IReadOnlyList<BookSnapshot> books;
        try
        {
            books = await _cache.LookupByAuthorAsync(_author.CanonicalId);
        }
        catch (Exception ex)
        {
            BooksLayout.Children.Clear();
            BooksLayout.Children.Add(new Label
            {
                Text = $"Couldn't load books: {ex.GetType().Name} — {ex.Message}",
                TextColor = ErrorColor,
                FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap,
            });
            return;
        }

        BooksLayout.Children.Clear();

        if (books.Count == 0)
        {
            BooksLayout.Children.Add(new Label
            {
                Text = "No books found in your library for this author.",
                FontSize = 14,
                TextColor = FadedInk,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0),
            });
            return;
        }

        foreach (var book in books)
        {
            BooksLayout.Children.Add(BuildBookCard(book));
        }
    }

    private View BuildBookCard(BookSnapshot book)
    {
        var titleLabel = new Label
        {
            Text = book.Title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Ink,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        var metaLabel = new Label
        {
            Text = FormatBookMeta(book),
            FontSize = 13,
            TextColor = FadedInk,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        var border = new Border
        {
            Stroke = Brass,
            StrokeThickness = 1,
            BackgroundColor = AgedParchment,
            Padding = new Thickness(14, 12),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { titleLabel, metaLabel },
            },
        };

        // Tap anywhere on the card to deep-link into the Web app's
        // book detail page — same pattern as ScanPage's FoundFrame
        // open-in-app button. Mobile companion stays read-only in v1.
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenInAppAsync(book.Id);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private static string FormatBookMeta(BookSnapshot book)
    {
        // Compose "★★★☆☆ · Read" — rating stars first if present,
        // then status. Skips the status segment if it's empty so
        // we don't render a dangling separator.
        var parts = new List<string>();
        if (book.Rating > 0)
        {
            var clamped = Math.Clamp(book.Rating, 0, 5);
            var filled = new string('★', clamped);
            var empty = new string('☆', 5 - clamped);
            parts.Add(filled + empty);
        }
        if (!string.IsNullOrWhiteSpace(book.Status))
        {
            parts.Add(book.Status);
        }
        return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
    }

    private async Task OpenInAppAsync(int bookId)
    {
        var url = $"{AppConfig.ApiBaseUrl}/books/{bookId}";
        try
        {
            await Launcher.Default.OpenAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            // Surface as a transient toast-style label below the
            // tapped card. Better than a silent failure on a tap.
            BooksLayout.Children.Insert(0, new Label
            {
                Text = $"Couldn't open browser: {ex.Message}",
                TextColor = ErrorColor,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
    }
}
