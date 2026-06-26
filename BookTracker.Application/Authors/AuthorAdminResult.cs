namespace BookTracker.Application.Authors;

/// <summary>Shared result for the /authors/{id} admin write commands
/// (<see cref="RenameAuthor"/>, <see cref="MarkAuthorAsAliasOf"/>,
/// <see cref="PromoteAuthorToCanonical"/>). Carries the success toast text or
/// the user-facing error (e.g. a name clash). A silent no-op (missing row,
/// empty input) returns <c>Success=false</c> with both messages null so the
/// page shows nothing — preserving the pre-refactor VM behaviour.</summary>
public record AuthorAdminResult(bool Success, string? SuccessMessage, string? ErrorMessage)
{
    public static AuthorAdminResult Ok(string message) => new(true, message, null);
    public static AuthorAdminResult Error(string message) => new(false, null, message);
    public static AuthorAdminResult NoOp => new(false, null, null);
}
