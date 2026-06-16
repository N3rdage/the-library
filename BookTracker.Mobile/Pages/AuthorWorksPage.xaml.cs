using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Theming;
using BookTracker.Shared.Catalog;
using Microsoft.Maui.Controls.Shapes;

namespace BookTracker.Mobile.Pages;

// The works you own by an author. Token-based + dark; replaces the old
// AuthorBooksPage. v1 shows owned works only (the "Missing" scope is online —
// TODO #54). Reached from the search flow with a runtime-chosen AuthorSnapshot.
public partial class AuthorWorksPage : ContentPage
{
    private readonly ICatalogCache _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly AuthorSnapshot _author;
    private bool _loaded;

    public AuthorWorksPage(ICatalogCache cache, IHttpClientFactory httpFactory, AuthorSnapshot author)
    {
        InitializeComponent();
        _cache = cache;
        _httpFactory = httpFactory;
        _author = author;
        Title = author.Name;
        HeaderLabel.Text = author.BookCount == 1
            ? "1 book in your library"
            : $"{author.BookCount:N0} books in your library";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return; // pushed page, fresh instance per visit — load once
        _loaded = true;
        await LoadWorksAsync();
    }

    private async Task LoadWorksAsync()
    {
        WorksLayout.Children.Clear();
        var spinner = new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center };
        spinner.SetThemeColor(ActivityIndicator.ColorProperty, "LeatherL", "LeatherD");
        WorksLayout.Children.Add(spinner);

        IReadOnlyList<BookSnapshot> books;
        try
        {
            books = await _cache.LookupByAuthorAsync(_author.CanonicalId);
        }
        catch (Exception ex)
        {
            WorksLayout.Children.Clear();
            var error = new Label
            {
                Text = $"Couldn't load books: {ex.GetType().Name} — {ex.Message}",
                FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap,
            };
            error.SetThemeColor(Label.TextColorProperty, "MissTagTxL", "MissTagTxD");
            WorksLayout.Children.Add(error);
            return;
        }

        WorksLayout.Children.Clear();

        if (books.Count == 0)
        {
            var empty = new Label
            {
                Text = "No books in your library for this author.",
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0),
            };
            empty.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");
            WorksLayout.Children.Add(empty);
            return;
        }

        foreach (var book in books)
        {
            WorksLayout.Children.Add(BuildBookCard(book));
        }
    }

    private View BuildBookCard(BookSnapshot book)
    {
        var titleLabel = new Label
        {
            Text = book.Title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        titleLabel.SetThemeColor(Label.TextColorProperty, "TextL", "TextD");

        var metaLabel = new Label
        {
            Text = FormatBookMeta(book),
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        metaLabel.SetThemeColor(Label.TextColorProperty, "TextMutedL", "TextMutedD");

        var coverPlaceholder = new Label
        {
            Text = "📖",
            FontSize = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        coverPlaceholder.SetThemeColor(Label.TextColorProperty, "BrassTextL", "BrassTextD");
        var coverImage = new Image { IsVisible = false, Aspect = Aspect.AspectFit };
        var coverSlot = new Grid
        {
            WidthRequest = 56,
            HeightRequest = 84,
            VerticalOptions = LayoutOptions.Start,
            Children = { coverPlaceholder, coverImage },
        };
        coverSlot.SetThemeColor(Grid.BackgroundColorProperty, "CoverMissL", "CoverMissD");

        var textStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, metaLabel },
        };

        var layout = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12,
        };
        layout.Add(coverSlot, 0, 0);
        layout.Add(textStack, 1, 0);

        var border = new Border
        {
            Style = (Style)Application.Current!.Resources["Card"],
            Padding = new Thickness(12, 10),
            Content = layout,
        };

        // Tap → deep-link to the web book detail (mobile stays read-only in v1).
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OpenInAppAsync(book.Id);
        border.GestureRecognizers.Add(tap);

        _ = Covers.LoadIntoAsync(_cache, _httpFactory, book.Id, coverImage, coverPlaceholder);
        return border;
    }

    // "★★★☆☆ · Read" — stars (if rated) then status, skipping empties.
    private static string FormatBookMeta(BookSnapshot book)
    {
        var parts = new List<string>();
        if (book.Rating > 0)
        {
            var clamped = Math.Clamp(book.Rating, 0, 5);
            parts.Add(new string('★', clamped) + new string('☆', 5 - clamped));
        }
        if (!string.IsNullOrWhiteSpace(book.Status))
            parts.Add(book.Status);
        return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
    }

    private async Task OpenInAppAsync(int bookId)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri($"{AppConfig.ApiBaseUrl}/books/{bookId}"));
        }
        catch (Exception ex)
        {
            var toast = new Label
            {
                Text = $"Couldn't open browser: {ex.Message}",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8),
            };
            toast.SetThemeColor(Label.TextColorProperty, "MissTagTxL", "MissTagTxD");
            WorksLayout.Children.Insert(0, toast);
        }
    }
}
