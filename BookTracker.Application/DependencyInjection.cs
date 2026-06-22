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
    /// Empty in PR0 — the scaffold proves the project + DI wiring compile and
    /// run green before any behaviour rides on top. Handlers land here from the
    /// Book pilot (PR1) onward. The registration strategy (per-handler vs a
    /// marker-interface assembly scan) is an open question to settle in PR1;
    /// see docs/BACKEND-REFACTOR-DESIGN.md.
    /// </remarks>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        return services;
    }
}
