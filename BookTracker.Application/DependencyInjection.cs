using Microsoft.Extensions.DependencyInjection;

namespace BookTracker.Application;

/// <summary>
/// Single registration entry point for the application layer. The Blazor host
/// calls <see cref="AddApplicationLayer"/> once; handlers self-register by
/// convention so adding a feature never touches the host (convention C2 — a
/// feature is one self-contained folder).
/// </summary>
public static class DependencyInjection
{
    private static readonly Type[] HandlerInterfaces =
        [typeof(ICommandHandler<>), typeof(ICommandHandler<,>)];

    /// <summary>
    /// Registers the dispatcher and every command handler in this assembly.
    /// </summary>
    /// <remarks>
    /// Handlers register by convention: any concrete type implementing
    /// <see cref="ICommandHandler{TCommand}"/> (or the two-arg form) is wired to
    /// that closed interface — implementing the interface IS the registration,
    /// so a new handler can't be left unregistered. No attribute (the interface
    /// is already the marker) and no MediatR. Everything is Scoped to match the
    /// DbContextFactory's per-operation context lifetime. Find every handler
    /// with <c>grep ": ICommandHandler&lt;"</c>.
    /// </remarks>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();

        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes()
                     .Where(t => t is { IsAbstract: false, IsInterface: false }))
            foreach (var iface in type.GetInterfaces()
                         .Where(i => i.IsGenericType
                                     && HandlerInterfaces.Contains(i.GetGenericTypeDefinition())))
                services.AddScoped(iface, type);

        return services;
    }
}
