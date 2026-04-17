using BookTracker.Data;
using BookTracker.Web.Components;
using BookTracker.Web.Services;
using BookTracker.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// TODO: evaluate a Blazor-native component library (MudBlazor, Radzen, FluentUI)
// instead of hand-rolling Bootstrap markup in every component. The current pages
// work but the genres picker, dialogs, and form feedback would all benefit.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Blazor Server circuits are long-lived while DbContext is scoped and not
// thread-safe, so components take IDbContextFactory<T> and create a short-lived
// context per operation rather than injecting DbContext directly.
builder.Services.AddDbContextFactory<BookTrackerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<IBookLookupService, BookLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("BookTracker/0.1 (+github.com/N3rdage/the-library)");
});

builder.Services.AddTransient<SeriesMatchService>();

builder.Services.Configure<AIAssistantOptions>(
    builder.Configuration.GetSection(AIAssistantOptions.SectionName));
builder.Services.AddScoped<IAIAssistantService, AIAssistantService>();

// ViewModels — transient so each component instance gets its own VM.
builder.Services.AddTransient<HomeViewModel>();
builder.Services.AddTransient<BookFormViewModel>();
builder.Services.AddTransient<EditionFormViewModel>();
builder.Services.AddTransient<CopyFormViewModel>();
builder.Services.AddTransient<GenrePickerViewModel>();
builder.Services.AddTransient<BookListViewModel>();
builder.Services.AddTransient<BookAddViewModel>();
builder.Services.AddTransient<BookEditViewModel>();
builder.Services.AddTransient<BulkAddViewModel>();
builder.Services.AddTransient<SeriesListViewModel>();
builder.Services.AddTransient<SeriesEditViewModel>();
builder.Services.AddTransient<ShoppingViewModel>();

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
