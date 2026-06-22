namespace BookTracker.Data;

/// <summary>
/// Thrown by an aggregate method when a caller asks it to enter an invalid
/// state (a rating outside 0–5, a blank title, removing a copy that isn't
/// part of the book). The <see cref="System.Exception.Message"/> is
/// user-safe — pages surface it directly in an error snackbar.
///
/// Lives in BookTracker.Data because aggregates throw it; the application
/// layer lets it propagate but never needs to construct it. Distinct from
/// the application layer's NotFoundException (a missing root, not an invalid
/// operation on a present one).
/// </summary>
public sealed class DomainRuleException(string message) : Exception(message);
