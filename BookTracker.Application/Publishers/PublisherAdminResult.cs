namespace BookTracker.Application.Publishers;

/// <summary>Shared result for the /publishers admin write commands
/// (<see cref="RenamePublisher"/>, <see cref="DeleteUnusedPublisher"/>,
/// <see cref="MergePublishers"/>). <paramref name="Changed"/> tells the page
/// whether to invalidate its drill-down cache and reload; <paramref name="Message"/>
/// is the single toast string the page shows for both successes and guard
/// refusals (e.g. a name clash or an in-use publisher) — mirroring the
/// pre-refactor VM, which surfaced both through one channel. A silent no-op
/// (missing row) returns <c>Changed=false</c> with a null message.</summary>
public record PublisherAdminResult(bool Changed, string? Message)
{
    public static PublisherAdminResult Done(string message) => new(true, message);
    public static PublisherAdminResult Refused(string message) => new(false, message);
    public static PublisherAdminResult NoOp => new(false, null);
}
