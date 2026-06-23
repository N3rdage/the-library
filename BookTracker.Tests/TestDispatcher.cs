using BookTracker.Application;
using BookTracker.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookTracker.Tests;

/// <summary>
/// Builds a real <see cref="IDispatcher"/> backed by the application-layer DI
/// registrations and a test's <see cref="TestDbContextFactory"/>, so ViewModel
/// tests exercise the same command-resolution path as production. Shared so the
/// many VM construction sites don't each re-spell the provider setup.
/// </summary>
internal static class TestDispatcher
{
    public static IDispatcher For(IDbContextFactory<BookTrackerDbContext> factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(factory);
        services.AddLogging(); // handlers may inject ILogger<T> (e.g. AddWishlistSeriesSlots)
        services.AddApplicationLayer();
        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }
}
