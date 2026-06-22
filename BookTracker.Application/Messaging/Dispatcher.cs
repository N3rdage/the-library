using Microsoft.Extensions.DependencyInjection;

namespace BookTracker.Application;

/// <summary>
/// Resolves the <see cref="ICommandHandler{TCommand}"/> (or
/// <see cref="ICommandHandler{TCommand, TResult}"/>) registered for a command's
/// concrete type and invokes it.
///
/// The invoke goes through <c>dynamic</c> rather than <c>MethodInfo.Invoke</c>
/// on purpose: reflection's Invoke wraps any exception a handler throws *before*
/// its first <c>await</c> in a <see cref="System.Reflection.TargetInvocationException"/>,
/// which would defeat the page's friendly-message snackbars and typed catches
/// like <c>catch (NotFoundException)</c>. The DLR call propagates the original
/// exception unwrapped, so correctness no longer depends on every handler
/// happening to await first. (No package needed — Microsoft.CSharp ships in the
/// shared framework.)
///
/// Scoped so it captures the resolving scope's provider (the Blazor circuit
/// scope in production), matching the handlers' Scoped lifetime.
///
/// One constraint the DLR imposes: the handler type and its command must be
/// accessible from this assembly — i.e. <b>public</b>. Every handler is a
/// <c>public sealed class</c> by convention (C4), so this holds; an
/// <c>internal</c> handler would fail to bind at runtime.
/// </summary>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    public Task Send(ICommand command, CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
        dynamic handler = services.GetRequiredService(handlerType);
        return handler.HandleAsync((dynamic)command, ct);
    }

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        dynamic handler = services.GetRequiredService(handlerType);
        return handler.HandleAsync((dynamic)command, ct);
    }
}
