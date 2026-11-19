namespace BookTracker.Mobile.Cache.Presentation;

// ISBN input handling for Bookshelf's Find tab (typed search + scanner).
// Lives here rather than in the page so the unit tests can cover it
// without the MAUI runtime.
public static class IsbnInput
{
    // 10–13 chars, digits with an optional trailing X (ISBN-10 check digit).
    public static bool LooksLikeIsbn(string q)
    {
        if (q.Length is < 10 or > 13) return false;
        for (int i = 0; i < q.Length; i++)
        {
            var c = q[i];
            if (char.IsDigit(c)) continue;
            if ((c is 'X' or 'x') && i == q.Length - 1) continue;
            return false;
        }
        return true;
    }

    // Strip to digits + a trailing ISBN-10 check 'X'. Shared by the typed path
    // and the scanner so a noisy barcode payload normalises the same way a typed
    // ISBN does before the cache lookup.
    public static string CleanIsbn(string raw) =>
        new string(raw.Where(c => char.IsDigit(c) || c is 'X' or 'x').ToArray());
}
