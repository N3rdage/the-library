using BookTracker.Application;
using BookTracker.Application.Books;
using Microsoft.Extensions.Logging;

namespace BookTracker.Web.Components.Shared;

/// <summary>
/// Shared eager-create logic for the two publisher fields — <see cref="EditionCopyForm"/>
/// on /books/add and <see cref="EditionFormDialog"/> on the book-detail edit. Each
/// used to carry its own near-identical copy; a regression in one (inverted
/// membership check, dropped catch) couldn't be caught by the other's tests. This
/// is the single source so one unit test guards both. TD-15a.
/// </summary>
internal static class PublisherEagerCreate
{
    /// <summary>
    /// When the user commits a publisher name via the CreatableAutocomplete (an
    /// explicit "Add …" selection), find-or-create it if its trimmed name isn't
    /// already in <paramref name="existingNames"/>. Returns (id, trimmed name) when
    /// a genuinely-new publisher was created — so the caller can append it to its
    /// cache — or null when nothing was created (blank input, an existing pick, or
    /// a swallowed best-effort fault). Never throws: the save's PublisherResolver
    /// net (Option B) still guarantees the row, so a transient fault or the
    /// accepted check-then-insert race (TD-15) just logs.
    /// </summary>
    public static async Task<(int Id, string Name)?> CreateIfNewAsync(
        string? value,
        IEnumerable<string> existingNames,
        IDispatcher dispatcher,
        ILogger logger)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (existingNames.Any(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            var id = await dispatcher.Send(new CreatePublisher(trimmed));
            return id is int newId ? (newId, trimmed) : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Eager publisher create failed for {Name}; the save will create it", trimmed);
            return null;
        }
    }
}
