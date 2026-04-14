using BookTracker.Data;
using BookTracker.Web.Components;
using BookTracker.Web.Services;
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

var app = builder.Build();

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
