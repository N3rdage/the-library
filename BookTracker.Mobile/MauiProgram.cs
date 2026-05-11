using BookTracker.Mobile.Cache;
using BookTracker.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace BookTracker.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
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

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
