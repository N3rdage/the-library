using BookTracker.Shared.Wishlist;

namespace BookTracker.Mobile.Cache.Presentation;

// The three sorts on Bookshelf's Wishlist tab.
public static class WishlistOrdering
{
    // High / Medium / everything-else (Low). "Low" catches any other server
    // priority string so no row is silently dropped.
    public static readonly (string Label, Func<WishlistItemSnapshot, bool> InBucket)[] PriorityBuckets =
    [
        ("High", i => i.Priority == "High"),
        ("Medium", i => i.Priority == "Medium"),
        ("Low", i => i.Priority != "High" && i.Priority != "Medium"),
    ];

    // High → Medium → Low(/other) buckets, each titled, rows by title.
    // Empty buckets are left out.
    public static List<(string Label, List<WishlistItemSnapshot> Rows)> ByPriority(IEnumerable<WishlistItemSnapshot> items)
    {
        var groups = new List<(string Label, List<WishlistItemSnapshot> Rows)>();
        foreach (var (label, inBucket) in PriorityBuckets)
        {
            var rows = items.Where(inBucket)
                .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rows.Count == 0) continue;
            groups.Add((label, rows));
        }
        return groups;
    }

    public static IEnumerable<WishlistItemSnapshot> ByAuthor(IEnumerable<WishlistItemSnapshot> items) =>
        items
            .OrderBy(i => i.Author, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase);

    // Series members first, grouped by series and in reading order;
    // standalone wishlist entries fall to the end, by title.
    public static IEnumerable<WishlistItemSnapshot> BySeries(IEnumerable<WishlistItemSnapshot> items) =>
        items
            .OrderBy(i => i.SeriesId is null)
            .ThenBy(i => i.SeriesId ?? int.MaxValue)
            .ThenBy(i => i.SeriesOrder ?? int.MaxValue)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase);
}
