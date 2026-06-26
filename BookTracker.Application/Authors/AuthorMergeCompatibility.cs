namespace BookTracker.Application.Authors;

/// <summary>The single rule deciding whether two authors may be merged. Both
/// must resolve to the same canonical — either directly (same
/// <c>CanonicalAuthorId</c>) or by one being an alias of the other. Anything
/// else is refused so a direct merge can't silently drop a pen-name
/// relationship; the user resolves aliases on /authors first.
///
/// Lives here so the write path (<see cref="MergeAuthorsHandler"/>, on entities)
/// and the read path (the merge-preview loader, on its read DTOs) share one
/// source of truth instead of each inlining the canonical-resolution rule.</summary>
public static class AuthorMergeCompatibility
{
    /// <summary>Returns null if the two authors may be merged, otherwise the
    /// user-facing reason they can't.</summary>
    public static string? Check(
        int aId, int? aCanonicalId, string aName,
        int bId, int? bCanonicalId, string bName)
    {
        if (aCanonicalId == bCanonicalId) return null;
        if (aCanonicalId == bId) return null;
        if (bCanonicalId == aId) return null;

        return $"\"{aName}\" and \"{bName}\" resolve to different canonical authors, so merging them directly would silently drop the pen-name relationship. Resolve the aliases on /authors first (promote one to canonical, or alias both to the same root), then come back to merge.";
    }
}
