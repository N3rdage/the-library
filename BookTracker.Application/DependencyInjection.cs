using BookTracker.Application.Books;
using Microsoft.Extensions.DependencyInjection;

namespace BookTracker.Application;

/// <summary>
/// Single registration entry point for the application layer. The Blazor host
/// calls <see cref="AddApplicationLayer"/> once; every command/query handler
/// registers itself from inside this project so adding a feature never touches
/// <c>ProgramSetup.cs</c> (convention C2 — a feature is one self-contained folder).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the application-layer handlers with the DI container.
    /// </summary>
    /// <remarks>
    /// Explicit per-handler registration — no marker-interface assembly scan
    /// (keeps the wiring greppable and on-ethos with the no-MediatR decision;
    /// revisit if the list gets unwieldy). Handlers are Scoped to match the
    /// DbContextFactory's per-operation context lifetime.
    /// </remarks>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        // Books feature
        services.AddScoped<RateBookHandler>();
        services.AddScoped<SetBookStatusHandler>();
        services.AddScoped<UpdateBookNotesHandler>();
        services.AddScoped<UpdateBookDetailsHandler>();
        services.AddScoped<AddEditionToBookHandler>();
        services.AddScoped<UpdateEditionHandler>();
        services.AddScoped<AddCopyToEditionHandler>();
        services.AddScoped<UpdateCopyHandler>();
        services.AddScoped<DeleteCopyHandler>();
        services.AddScoped<DeleteBookHandler>();
        services.AddScoped<SetEditionCoverHandler>();

        return services;
    }
}
