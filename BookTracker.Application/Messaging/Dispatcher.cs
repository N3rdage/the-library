using Microsoft.Extensions.DependencyInjection;

namespace BookTracker.Application;

/// <summary>
/// Resolves the <see cref="ICommandHandler{TCommand}"/> (or
/// <see cref="ICommandHandler{TCommand, TResult}"/>) registered for a command's
/// concrete type and invokes it. This one reflection hop is the entire cost of
/// the dispatcher indirection — and the only "magic" in the command pipeline.
/// Scoped so it captures the resolving scope's provider (the Blazor circuit
/// scope in production), matching the handlers' Scoped lifetime.
/// </summary>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task Send(ICommand command, CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        var handler = services.GetRequiredService(handlerType);
        return (Task)Invoke(handlerType, handler, command, ct);
    }

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = services.GetRequiredService(handlerType);
        return (Task<TResult>)Invoke(handlerType, handler, command, ct);
    }

    // Every handler interface declares exactly one method, HandleAsync(command, ct),
    // so the lookup is unambiguous.
    private static object Invoke(Type handlerType, object handler, object command, CancellationToken ct) =>
        handlerType.GetMethod("HandleAsync")!.Invoke(handler, [command, ct])!;
}
