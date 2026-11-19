using System.Globalization;
using System.Text;
using BookTracker.Shared.Catalog;
using BookTracker.Shared.Online;

namespace BookTracker.Mobile.Cache.Presentation;

/// <summary>One row of Bookshelf's Author-works list. <see cref="OwnedBook"/>
/// is the cached Book for an owned row; <see cref="OwnedBookId"/> is set on
/// every owned row, including one Bookcase reports as owned that the phone's
/// cache does not hold yet (then <see cref="OwnedBook"/> is null). A row with
/// no <see cref="OwnedBookId"/> is a work the library does not own.</summary>
public sealed record AuthorWorkRow(
    string Title,
    int? OwnedBookId,
    BookSnapshot? OwnedBook,
    int? FirstPublishYear)
{
    public bool IsOwned => OwnedBookId is not null;
}

/// <summary>Merges the author's owned works from the cache with the Open
/// Library bibliography from Bookcase's GET /api/online/author-works.</summary>
public static class AuthorWorksMerger
{
    /// <summary>The meta line under an owned row the phone's cache does not
    /// hold yet.</summary>
    public const string NotSyncedMeta = "In your library  ·  sync to see it here";

    /// <summary>The hint "Missing only" shows when every listed work is
    /// owned.</summary>
    public const string NothingMissingHint = "You own every work listed for this author.";

    /// <summary>The owned Books in cache order, then the online works the
    /// library does not own in the endpoint's order. An online work is
    /// dropped as already listed when its <c>OwnedBookId</c> is a cached
    /// Book, or when its normalised title matches a cached Book's title or
    /// one of its Works' titles (so a stale cache or a missing server match
    /// does not double-list). An online work Bookcase reports as owned but
    /// the cache lacks becomes an owned row without a cached Book. Online
    /// works without a title, and repeats of a normalised title, are
    /// dropped.</summary>
    public static IReadOnlyList<AuthorWorkRow> Merge(
        IReadOnlyList<BookSnapshot> owned,
        IReadOnlyList<OnlineAuthorWork>? online)
    {
        var rows = new List<AuthorWorkRow>(owned.Count + (online?.Count ?? 0));
        var ownedIds = new HashSet<int>();
        var seenTitles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var book in owned)
        {
            rows.Add(new AuthorWorkRow(book.Title, book.Id, book, null));
            ownedIds.Add(book.Id);
            AddTitle(seenTitles, book.Title);
            if (book.Works is { } works)
                foreach (var work in works)
                    AddTitle(seenTitles, work.Title);
        }

        if (online is null) return rows;

        foreach (var work in online)
        {
            if (work.OwnedBookId is int id && ownedIds.Contains(id)) continue;

            var key = NormaliseTitle(work.Title);
            if (key.Length == 0 || !seenTitles.Add(key)) continue;

            if (work.OwnedBookId is int staleId) ownedIds.Add(staleId);
            rows.Add(new AuthorWorkRow(work.Title!.Trim(), work.OwnedBookId, null, work.FirstPublishYear));
        }

        return rows;
    }

    /// <summary>The rows the "All · Missing only" toggle shows: every row, or
    /// only those the library does not own.</summary>
    public static IReadOnlyList<AuthorWorkRow> Filter(IReadOnlyList<AuthorWorkRow> rows, bool missingOnly) =>
        missingOnly ? rows.Where(r => !r.IsOwned).ToList() : rows;

    /// <summary>"12 works · 5 owned" for the page header once the online
    /// list has arrived.</summary>
    public static string Summary(IReadOnlyList<AuthorWorkRow> rows)
    {
        var owned = rows.Count(r => r.IsOwned);
        var works = rows.Count == 1 ? "1 work" : $"{rows.Count:N0} works";
        return $"{works} · {owned:N0} owned";
    }

    /// <summary>The meta line under a not-owned row: "Not owned", with the
    /// first-publish year when Open Library has one.</summary>
    public static string MissingMeta(AuthorWorkRow row) =>
        row.FirstPublishYear is int year
            ? $"Not owned  ·  {year.ToString(CultureInfo.InvariantCulture)}"
            : "Not owned";

    /// <summary>The title as compared: lower case, punctuation and symbols as
    /// spaces, whitespace collapsed, a leading "the", "a" or "an" dropped. The
    /// same rule Bookcase uses for its owned-work match.</summary>
    public static string NormaliseTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";

        var sb = new StringBuilder(title.Length);
        var pendingSpace = false;
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
            }
            else
            {
                pendingSpace = true;
            }
        }

        var collapsed = sb.ToString();
        foreach (var article in Articles)
        {
            if (collapsed.StartsWith(article, StringComparison.Ordinal) && collapsed.Length > article.Length)
                return collapsed[article.Length..];
        }
        return collapsed;
    }

    private static readonly string[] Articles = ["the ", "an ", "a "];

    private static void AddTitle(HashSet<string> seen, string? title)
    {
        var key = NormaliseTitle(title);
        if (key.Length > 0) seen.Add(key);
    }
}
