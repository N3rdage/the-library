using BookTracker.Data;
using BookTracker.Web.Components;
using BookTracker.Web.Services;
using BookTracker.Web.Telemetry;
using BookTracker.Web.ViewModels;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace BookTracker.Web;

/// <summary>
/// Extracted from Program.cs's top-level statements so the
/// Playwright test fixture can build a fully-configured
/// WebApplication instance without duplicating the DI / middleware
/// wiring. Program.Main becomes a thin wrapper:
/// build → migrate → run. Tests call Build directly and skip the
/// migrations step (the Testcontainer DB has the schema applied
/// once-per-run via SqlServerContainer.cs).
/// </summary>
public static class ProgramSetup
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);
        var app = builder.Build();
        ConfigureMiddleware(app);
        return app;
    }

    public static async Task RunMigrationsAsync(WebApplication app)
    {
        // TODO: replace migrate-on-startup with a dedicated deploy-time
        // migration step (e.g. `dotnet ef migrations bundle` run from the
        // GitHub Actions workflow) once the app goes multi-instance or
        // needs zero-downtime deploys. For now, the single-instance App
        // Service makes this simple and safe.
        using var scope = app.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BookTrackerDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        // Application Insights: explicit SDK registration (the App Service codeless
        // attach gives baseline request/dependency telemetry but NOT structured
        // ILogger output — message-template properties like {Isbn} only become
        // custom dimensions once the SDK is wired here). Connection string
        // resolves from APPLICATIONINSIGHTS_CONNECTION_STRING (set in
        // app-config.bicep).
        builder.Services.AddApplicationInsightsTelemetry();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ITelemetryInitializer, UserTelemetryInitializer>();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // MudBlazor — pilot on Home + MergeBook pages. Coexists with Bootstrap
        // (both stylesheets loaded in App.razor) for the duration of the pilot.
        builder.Services.AddMudServices();

        // Increase SignalR max message size for photo ISBN capture (base64 images).
        builder.Services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 512 * 1024; // 512KB
        });

        // Blazor Server circuits are long-lived while DbContext is scoped and not
        // thread-safe, so components take IDbContextFactory<T> and create a short-lived
        // context per operation rather than injecting DbContext directly.
        builder.Services.AddDbContextFactory<BookTrackerDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

        builder.Services.Configure<TroveOptions>(
            builder.Configuration.GetSection(TroveOptions.SectionName));

        builder.Services.AddHttpClient<IBookLookupService, BookLookupService>(client =>
        {
            // Open Library can be slow under load — 10s was on the edge and caused
            // 2-3 of 5 lookups to time out in real bulk-scan use. 25s gives the
            // upstream APIs more headroom while still capping the worst case so a
            // hung connection doesn't block the UI indefinitely.
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BookTracker/0.1 (+github.com/N3rdage/the-library)");
        });

        builder.Services.AddTransient<SeriesMatchService>();

        builder.Services.AddScoped<IDuplicateDetectionService, DuplicateDetectionService>();
        builder.Services.AddScoped<IAuthorMergeService, AuthorMergeService>();
        builder.Services.AddScoped<IWorkMergeService, WorkMergeService>();
        builder.Services.AddScoped<IWorkSearchService, WorkSearchService>();
        builder.Services.AddScoped<IEditionMergeService, EditionMergeService>();
        builder.Services.AddScoped<IBookMergeService, BookMergeService>();

        // One-shot startup task that re-classifies existing Editions using the
        // richer BookFormat enum (populated from upstream metadata). Idempotent via
        // a MaintenanceLog marker; safe to leave registered after the backfill has
        // run.
        builder.Services.AddHostedService<EditionFormatBackfillService>();

        builder.Services.Configure<AIOptions>(
            builder.Configuration.GetSection(AIOptions.SectionName));
        builder.Services.AddScoped<AIProviderFactory>(sp =>
        {
            var factory = new AIProviderFactory(
                sp.GetRequiredService<IDbContextFactory<BookTrackerDbContext>>(),
                sp.GetRequiredService<IOptions<AIOptions>>(),
                sp.GetRequiredService<ILoggerFactory>());
            factory.Initialize();
            return factory;
        });
        builder.Services.AddScoped<IAIAssistantService>(sp =>
            sp.GetRequiredService<AIProviderFactory>().GetService());

        // ViewModels — transient so each component instance gets its own VM.
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<BookFormViewModel>();
        builder.Services.AddTransient<EditionFormViewModel>();
        builder.Services.AddTransient<CopyFormViewModel>();
        builder.Services.AddTransient<GenrePickerViewModel>();
        builder.Services.AddTransient<MudGenrePickerViewModel>();
        builder.Services.AddTransient<BookListViewModel>();
        builder.Services.AddTransient<BookAddViewModel>();
        builder.Services.AddTransient<BookEditViewModel>();
        builder.Services.AddTransient<BookDetailViewModel>();
        builder.Services.AddTransient<BookEditDialogViewModel>();
        builder.Services.AddTransient<WorkEditDialogViewModel>();
        builder.Services.AddTransient<EditionFormDialogViewModel>();
        builder.Services.AddTransient<CopyFormDialogViewModel>();
        builder.Services.AddTransient<BulkAddViewModel>();
        builder.Services.AddTransient<SeriesListViewModel>();
        builder.Services.AddTransient<SeriesEditViewModel>();
        builder.Services.AddTransient<AuthorListViewModel>();
        builder.Services.AddTransient<PublisherListViewModel>();
        builder.Services.AddTransient<ShoppingViewModel>();
        builder.Services.AddTransient<DuplicatesViewModel>();
        builder.Services.AddTransient<AuthorMergeViewModel>();
        builder.Services.AddTransient<WorkMergeViewModel>();
        builder.Services.AddTransient<EditionMergeViewModel>();
        builder.Services.AddTransient<BookMergeViewModel>();
        builder.Services.AddScoped<AIAssistantViewModel>();
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        // 404s re-execute through /NotFound so the user lands on the friendly
        // "page wandered off the shelf" view rather than the framework default.
        // Outside the if-block so dev gets the friendly page too — the dev
        // exception page only handles thrown exceptions, not 404s.
        app.UseStatusCodePagesWithReExecute("/NotFound");

        app.UseHttpsRedirection();

        // Baseline security response headers. CSP lives in App.razor as a <meta>
        // tag; the three below need to be HTTP-header-only (meta-tag equivalents
        // are either deprecated or ignored by modern browsers).
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Frame-Options"] = "DENY";                     // clickjacking
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";           // MIME sniffing
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });

        app.UseAntiforgery();

        // MapStaticAssets resolves a manifest file named after the entry
        // assembly (BookTracker.Web.staticwebassets.endpoints.json). Test
        // hosts run as BookTracker.Tests so the file isn't present; the
        // Razor pages tested via Playwright don't need cache-busted asset
        // URLs to render and assert against, so silent skip is fine.
        try
        {
            app.MapStaticAssets();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("staticwebassets.endpoints.json"))
        {
            // Expected when running under a test host. Production startup
            // would have the manifest; absence there would be a real bug.
        }

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
