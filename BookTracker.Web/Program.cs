using BookTracker.Data;
using BookTracker.Web.Components;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// TODO: evaluate a Blazor-native component library (MudBlazor, Radzen, FluentUI)
// instead of hand-rolling Bootstrap markup in every component. The current pages
// work but the genres picker, dialogs, and form feedback would all benefit.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor — pilot on Home + MergeBook pages. Coexists with Bootstrap
// (both stylesheets loaded in App.razor) for the duration of the pilot.
// If we commit to migrating fully, Bootstrap + hand-rolled cards come out
// as pages are rewritten.
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
    client.Timeout = TimeSpan.FromSeconds(10);
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
        sp.GetRequiredService<IOptions<AIOptions>>());
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
builder.Services.AddTransient<ShoppingViewModel>();
builder.Services.AddTransient<DuplicatesViewModel>();
builder.Services.AddTransient<AuthorMergeViewModel>();
builder.Services.AddTransient<WorkMergeViewModel>();
builder.Services.AddTransient<EditionMergeViewModel>();
builder.Services.AddTransient<BookMergeViewModel>();
builder.Services.AddScoped<AIAssistantViewModel>();

var app = builder.Build();

// TODO: replace migrate-on-startup with a dedicated deploy-time migration step
// (e.g. `dotnet ef migrations bundle` run from the GitHub Actions workflow)
// once the app goes multi-instance or needs zero-downtime deploys. For now,
// the single-instance App Service makes this simple and safe.
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BookTrackerDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// TODO: replace the default /Error page and exception handler with proper error
// handling — structured logging, user-friendly messages by category, correlation
// ids surfaced to the user, and separate 404 handling.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
