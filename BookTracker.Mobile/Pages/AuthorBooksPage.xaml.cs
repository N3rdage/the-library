using BookTracker.Mobile.Cache;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

public partial class AuthorBooksPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AuthorSnapshot _author;
    private bool _loaded;

    private static readonly Color Brass = Color.FromArgb("#A67B3A");
    private static readonly Color AgedParchment = Color.FromArgb("#F2EADB");
    private static readonly Color CoverPlaceholderBg = Color.FromArgb("#E0DAC8");
    private static readonly Color Ink = Color.FromArgb("#2C2416");
    private static readonly Color FadedInk = Color.FromArgb("#6B5D4A");
    private static readonly Color ErrorColor = Color.FromArgb("#9B3B2E");

    public AuthorBooksPage(ICatalogCache cache, IHttpClientFactory httpFactory, AuthorSnapshot author)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
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

        // Cover thumbnail (left column). Image starts hidden; the
        // EnsureCoverCachedAsync call below swaps it in when the
        // bytes land on disk. Until then the parchment-grey square
        // shows a faded glyph so the row layout doesn't jump.
        var coverPlaceholder = new Label
        {
            Text = "📖",
            FontSize = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Brass,
        };
        var coverImage = new Image
        {
            IsVisible = false,
            Aspect = Aspect.AspectFit,
        };
        var coverSlot = new Grid
        {
            WidthRequest = 56,
            HeightRequest = 84,
            BackgroundColor = CoverPlaceholderBg,
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, metaLabel },
        };

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 12,
        };
        layout.Add(coverSlot, 0, 0);
        layout.Add(textStack, 1, 0);

        var border = new Border
        {
            Stroke = Brass,
            StrokeThickness = 1,
            BackgroundColor = AgedParchment,
            Padding = new Thickness(12, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = layout,
        };

        // Tap anywhere on the card to deep-link into the Web app's
        // book detail page — same pattern as ScanPage's FoundFrame
        // open-in-app button. Mobile companion stays read-only in v1.
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenInAppAsync(book.Id);
        border.GestureRecognizers.Add(tap);

        // Fire the cover fetch off without awaiting — the row is
        // visible immediately with the placeholder, and the image
        // slot swaps in when the cache lands. Captures bookId rather
        // than the BookSnapshot record to keep the closure small.
        _ = LoadCoverAsync(book.Id, coverImage, coverPlaceholder);

        return border;
    }

    private async Task LoadCoverAsync(int bookId, Image target, Label placeholder)
    {
        try
        {
            var http = _httpFactory.CreateClient("covers");
            var path = await _cache.EnsureCoverCachedAsync(bookId, http);
            if (path is null) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                target.Source = ImageSource.FromFile(path);
                target.IsVisible = true;
                placeholder.IsVisible = false;
            });
        }
        catch
        {
            // Fall through to the placeholder — the next render of
            // this row (page revisit / scroll) will retry the fetch
            // through EnsureCoverCachedAsync.
        }
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
