namespace BookTracker.Mobile.Cache.Presentation;

// Pill captions for Bookshelf's Gaps tab.
public static class SeriesGapFormatter
{
    // Missing slots as pill captions: a single "#N" per slot, but a run of
    // three or more collapses to one "#2–#4" range pill so a series the user
    // is far behind on doesn't render as a wall of pills.
    public static List<string> MissingParts(IReadOnlyList<int> orders)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < orders.Count)
        {
            int start = orders[i];
            int end = start;
            while (i + 1 < orders.Count && orders[i + 1] == end + 1)
            {
                end = orders[i + 1];
                i++;
            }
            if (end - start + 1 >= 3)
                parts.Add($"#{start}–#{end}");
            else
                for (int n = start; n <= end; n++) parts.Add($"#{n}");
            i++;
        }
        return parts;
    }
}
