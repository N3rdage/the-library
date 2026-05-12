using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Pages;
using BookTracker.Mobile.Services;
using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;

namespace BookTracker.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            // ZXing.Net.MAUI handler — registers the CameraBarcodeReaderView
            // control so the XAML namespace lights up at runtime.
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Auth + API services.
        // AuthService is a singleton — MSAL's PublicClientApplication
        // owns the token cache and should outlive any one page. The
        // HttpClient also stays singleton-ish (typed-client pattern)
        // so the underlying HttpMessageHandler is reused.
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddHttpClient<IApiClient, ApiClient>(c =>
        {
            c.BaseAddress = new Uri(AppConfig.ApiBaseUrl);
        });

        // Offline catalog cache. Singleton because the SQLite
        // connection is per-DB-file and we want one open handle for
        // the lifetime of the app process. Init is idempotent + lazy
        // — callers (currently MainPage.OnAppearing) call InitAsync
        // before any other method on first use.
        builder.Services.AddSingleton<ICatalogCache, CatalogCache>();

        builder.Services.AddSingleton<MainPage>();
        // Scan page is transient — every navigation gets a fresh
        // CameraBarcodeReaderView so we don't hold the camera open
        // when the page isn't visible.
        builder.Services.AddTransient<ScanPage>();
        // Author search page is transient so each navigation gets a
        // fresh debounce state + result list. AuthorBooksPage isn't
        // registered — it's constructed inline because it needs a
        // runtime-chosen AuthorSnapshot that DI can't supply.
        builder.Services.AddTransient<AuthorSearchPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
