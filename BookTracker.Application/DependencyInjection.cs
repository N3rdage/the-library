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
    /// The dispatcher + explicit per-handler registration against
    /// <see cref="ICommandHandler{TCommand}"/> — no assembly scan (one line per
    /// command is the price of no magic; the list stays greppable). Consumers
    /// inject a single <see cref="IDispatcher"/> rather than each handler.
    /// Everything is Scoped to match the DbContextFactory's per-operation
    /// context lifetime.
    /// </remarks>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();

        // Books feature
        services.AddScoped<ICommandHandler<MarkBookRead>, MarkBookReadHandler>();
        services.AddScoped<ICommandHandler<RateBook>, RateBookHandler>();
        services.AddScoped<ICommandHandler<SetBookStatus>, SetBookStatusHandler>();
        services.AddScoped<ICommandHandler<UpdateBookNotes>, UpdateBookNotesHandler>();
        services.AddScoped<ICommandHandler<UpdateBookDetails>, UpdateBookDetailsHandler>();
        services.AddScoped<ICommandHandler<AddEditionToBook, int>, AddEditionToBookHandler>();
        services.AddScoped<ICommandHandler<UpdateEdition>, UpdateEditionHandler>();
        services.AddScoped<ICommandHandler<AddCopyToEdition, int>, AddCopyToEditionHandler>();
        services.AddScoped<ICommandHandler<UpdateCopy>, UpdateCopyHandler>();
        services.AddScoped<ICommandHandler<DeleteCopy>, DeleteCopyHandler>();
        services.AddScoped<ICommandHandler<DeleteBook>, DeleteBookHandler>();
        services.AddScoped<ICommandHandler<SetEditionCover>, SetEditionCoverHandler>();

        return services;
    }
}
