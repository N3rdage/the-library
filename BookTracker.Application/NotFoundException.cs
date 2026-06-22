namespace BookTracker.Application;

/// <summary>
/// Thrown by a handler when the aggregate root it was asked to act on
/// doesn't exist (or is soft-deleted, and thus invisible behind the global
/// query filter). Pages map it to a "not found" snackbar. Distinct from
/// <see cref="BookTracker.Data.DomainRuleException"/> — that's a present root
/// asked to do something illegal; this is a root that isn't there at all.
/// </summary>
public sealed class NotFoundException(string message) : Exception(message);
