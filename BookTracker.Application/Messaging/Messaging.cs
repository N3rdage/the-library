namespace BookTracker.Application;

/// <summary>Marker for a command with no return value.</summary>
public interface ICommand;

/// <summary>Marker for a command that returns <typeparamref name="TResult"/>.</summary>
public interface ICommand<TResult>;

/// <summary>Handles a no-result command.</summary>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken ct = default);
}

/// <summary>Handles a command that returns a value.</summary>
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
}

/// <summary>
/// The single entry point a consumer injects to invoke commands — one
/// <c>IDispatcher</c> instead of one handler per command, so a ViewModel's
/// constructor doesn't grow with every command it touches. Resolves the
/// handler registered for the command's concrete type from DI.
///
/// Deliberately thin: it has no pipeline/behaviour stage. If cross-cutting
/// concerns (logging, validation, a transaction envelope) ever need to wrap
/// every command, this is the one place to add them — see
/// docs/BACKEND-REFACTOR-DESIGN.md.
/// </summary>
public interface IDispatcher
{
    Task Send(ICommand command, CancellationToken ct = default);
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default);
}
