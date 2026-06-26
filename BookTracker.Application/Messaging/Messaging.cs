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

/// <summary>Marker for a read query that returns <typeparamref name="TResult"/>.
/// Kept distinct from <see cref="ICommand{TResult}"/> so the read/write split
/// (C5) is visible at the call site: a query never mutates, projects to a
/// read-model DTO with <c>AsNoTracking()</c>, and never loads a write
/// aggregate.</summary>
public interface IQuery<TResult>;

/// <summary>Handles a read query.</summary>
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
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

    /// <summary>Runs a read query through its registered handler. Same single
    /// seam as <see cref="Send{TResult}"/> on the write side, so a ViewModel
    /// injects one <c>IDispatcher</c> for both reads and writes instead of one
    /// handler per query.</summary>
    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}
