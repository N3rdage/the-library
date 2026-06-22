using BookTracker.Application.Authors;
using BookTracker.Data;
using BookTracker.Data.Models;
using BookTracker.Shared.Catalog;
using BookTracker.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Web.ViewModels;

// View model for /wishlist (formerly /shopping). Three jobs:
//
//   1. Search-and-add (PR B) — type an ISBN or title/author, pick from
//      external lookup candidates, add to the wishlist with cover URL +
//      known ISBNs. Reuses IBookLookupService for both paths.
//   2. Series gaps — surface incomplete numbered series so the user can
//      decide what to seek. PR C will turn the gaps into bulk-wishlist
//      additions; today the section is read-only.
//   3. Wishlist management — manual quick-add (still useful when search
//      doesn't find the right candidate), list display, mark-as-bought
//      (creates Book + Edition + Copy with the follow-up tag), remove.
//
// The "Do I have this?" search that lived here pre-2026-05-25 was
// deleted in PR A — duplicate of /bookshop's scan + Bookshelf ScanPage.
public class WishlistViewModel(
    IDbContextFactory<BookTrackerDbContext> dbFactory,
    IBookLookupService lookup,
    ILogger<WishlistViewModel> logger)
{
    // ---- Search-and-add (PR B) ----

    public string SearchQuery { get; set; } = "";
    public bool Searching { get; private set; }
    public string? SearchError { get; private set; }

    /// <summary>True when the user has toggled the field-scoped search
    /// expander. While open, the simple single-box query is hidden in
    /// favour of separate Title / Author / ISBN inputs — solves the
    /// "Martin Grant" ambiguity (Open Library's relevance ranks title
    /// hits before author hits when both fields could match).</summary>
    public bool AdvancedSearchOpen { get; set; }

    public string AdvancedTitle { get; set; } = "";
    public string AdvancedAuthor { get; set; } = "";
    public string AdvancedIsbn { get; set; } = "";

    /// <summary>Candidates from the most recent search. Empty before any
    /// search has run; empty + SearchedOnce=true means the search ran
    /// and found nothing.</summary>
    public List<WishlistCandidate> SearchCandidates { get; private set; } = [];

    /// <summary>True after at least one search has been issued — drives
    /// the "no matches" message vs the initial empty-state prompt.</summary>
    public bool SearchedOnce { get; private set; }

    public async Task SearchAsync(CancellationToken ct = default)
    {
        var raw = (SearchQuery ?? "").Trim();
        if (string.IsNullOrEmpty(raw)) return;

        Searching = true;
        SearchError = null;
        SearchCandidates = [];
        try
        {
            // ISBN-shaped input (10-13 digits, allowing trailing X for
            // the ISBN-10 check digit) → single-result lookup. Otherwise
            // treat the whole string as a title/author query.
            var cleaned = new string(raw.Where(c => char.IsLetterOrDigit(c)).ToArray());
            var isIsbn = cleaned.Length is >= 10 and <= 13
                && cleaned.All(c => char.IsDigit(c) || c is 'X' or 'x');

            if (isIsbn)
            {
                var hit = await lookup.LookupByIsbnAsync(cleaned, ct);
                if (hit is not null)
                {
                    // Duplicate-detection for the ISBN path: surface a
                    // warning badge if the user already owns this ISBN
                    // (any Edition.Isbn) or has it on the wishlist (legacy
                    // single column OR the new WishlistItemIsbn table).
                    // Doesn't block Add — the user might want a backup copy
                    // on the wishlist or be deliberately re-adding — just
                    // tells them before they click. Text-search candidates
                    // don't get this check (no ISBN to match against).
                    var (ownedBookId, wishlistedItemId) =
                        await FindDuplicateMatchesAsync(cleaned, ct);

                    SearchCandidates = [new WishlistCandidate(
                        Title: hit.Title,
                        Author: hit.Author,
                        Isbns: string.IsNullOrWhiteSpace(hit.Isbn) ? [] : [hit.Isbn],
                        CoverUrl: hit.CoverUrl,
                        Source: hit.Source,
                        AlreadyOwnedBookId: ownedBookId,
                        AlreadyWishlistedItemId: wishlistedItemId)];
                }
            }
            else
            {
                // SearchByTitleAuthorAsync was originally for pre-ISBN
                // books but the underlying Open Library search works on
                // any title. Candidates don't carry ISBNs (BookSearchCandidate
                // is work-level, not edition-level) — wishlist rows added
                // this way go in ISBN-less, scan-flag in PR D simply
                // won't fire for them. Acceptable for v1.
                var hits = await lookup.SearchByTitleAuthorAsync(raw, author: null, ct);
                SearchCandidates = hits
                    .Select(c => new WishlistCandidate(
                        Title: c.Title,
                        Author: c.Author,
                        Isbns: [],
                        CoverUrl: c.CoverUrl,
                        Source: "Open Library"))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Wishlist search failed for query {Query}", raw);
            SearchError = "Search failed — the lookup service didn't respond. Try again, or use Quick add below.";
        }
        finally
        {
            Searching = false;
            SearchedOnce = true;
        }
    }

    /// <summary>Field-scoped variant of SearchAsync. ISBN field wins if
    /// filled (single-result lookup with the same duplicate-detection as
    /// SearchAsync's ISBN branch); otherwise the typed Title + Author go
    /// to SearchByTitleAuthorAsync as separate fields — distinguishing
    /// "Martin Grant" the title-substring match from "Martin Grant" the
    /// author. At least one field must be non-empty.</summary>
    public async Task SearchAdvancedAsync(CancellationToken ct = default)
    {
        var title = (AdvancedTitle ?? "").Trim();
        var author = (AdvancedAuthor ?? "").Trim();
        var rawIsbn = (AdvancedIsbn ?? "").Trim();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(author) && string.IsNullOrEmpty(rawIsbn)) return;

        Searching = true;
        SearchError = null;
        SearchCandidates = [];
        try
        {
            if (!string.IsNullOrEmpty(rawIsbn))
            {
                var cleaned = new string(rawIsbn.Where(c => char.IsLetterOrDigit(c)).ToArray());
                var isIsbn = cleaned.Length is >= 10 and <= 13
                    && cleaned.All(c => char.IsDigit(c) || c is 'X' or 'x');
                if (!isIsbn)
                {
                    SearchError = "ISBN must be 10 or 13 digits (optional trailing X for ISBN-10).";
                    return;
                }
                var hit = await lookup.LookupByIsbnAsync(cleaned, ct);
                if (hit is not null)
                {
                    var (ownedBookId, wishlistedItemId) =
                        await FindDuplicateMatchesAsync(cleaned, ct);
                    SearchCandidates = [new WishlistCandidate(
                        Title: hit.Title,
                        Author: hit.Author,
                        Isbns: string.IsNullOrWhiteSpace(hit.Isbn) ? [] : [hit.Isbn],
                        CoverUrl: hit.CoverUrl,
                        Source: hit.Source,
                        AlreadyOwnedBookId: ownedBookId,
                        AlreadyWishlistedItemId: wishlistedItemId)];
                }
            }
            else
            {
                // Both title + author land as separate fields on Open Library's
                // structured search — solves the simple-box ambiguity for the
                // "Martin Grant" case (author-scoped query no longer competes
                // with title hits for "Martin" / "Grant").
                var hits = await lookup.SearchByTitleAuthorAsync(
                    string.IsNullOrEmpty(title) ? null : title,
                    string.IsNullOrEmpty(author) ? null : author,
                    ct);
                SearchCandidates = hits
                    .Select(c => new WishlistCandidate(
                        Title: c.Title,
                        Author: c.Author,
                        Isbns: [],
                        CoverUrl: c.CoverUrl,
                        Source: "Open Library"))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Advanced wishlist search failed (Title={Title}, Author={Author}, Isbn={Isbn})", title, author, rawIsbn);
            SearchError = "Search failed — the lookup service didn't respond. Try again, or use Quick add below.";
        }
        finally
        {
            Searching = false;
            SearchedOnce = true;
        }
    }

    /// <summary>Returns (existing Book.Id if owned, existing WishlistItem.Id
    /// if wishlisted) for the given ISBN, or (null, null) for neither.
    /// Owned check hits Edition.Isbn (filtered-unique index → seek). Wishlist
    /// check unions the legacy single column with the new WishlistItemIsbn
    /// table so both shapes of wishlist row are caught.</summary>
    private async Task<(int? OwnedBookId, int? WishlistedItemId)> FindDuplicateMatchesAsync(
        string isbn, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var ownedBookId = await db.Editions
            .Where(e => e.Isbn == isbn)
            .Select(e => (int?)e.BookId)
            .FirstOrDefaultAsync(ct);

        var wishlistedItemId = await db.WishlistItems
            .Where(w => w.Isbn == isbn || w.Isbns.Any(i => i.Isbn == isbn))
            .Select(w => (int?)w.Id)
            .FirstOrDefaultAsync(ct);

        return (ownedBookId, wishlistedItemId);
    }

    public void ClearSearch()
    {
        SearchQuery = "";
        AdvancedTitle = "";
        AdvancedAuthor = "";
        AdvancedIsbn = "";
        SearchCandidates = [];
        SearchedOnce = false;
        SearchError = null;
    }

    /// <summary>Add a search candidate to the wishlist. Captures title,
    /// author, cover URL, and every known ISBN (both the legacy single-
    /// column for back-compat display AND the new WishlistItemIsbn table
    /// for the PR D scan-flag lookup). Returns the new wishlist row, or
    /// null when the candidate has no usable title (defensive — search
    /// candidates without titles are filtered by the UI but the VM is
    /// the single source of truth).</summary>
    public async Task<WishlistRow?> AddCandidateAsync(WishlistCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Title)) return null;

        await using var db = await dbFactory.CreateDbContextAsync();

        var primaryIsbn = candidate.Isbns.FirstOrDefault();
        var item = new WishlistItem
        {
            Title = candidate.Title.Trim(),
            Author = string.IsNullOrWhiteSpace(candidate.Author) ? "Unknown" : candidate.Author.Trim(),
            Priority = WishlistPriority.Medium,
            Isbn = primaryIsbn, // legacy column — primary ISBN for back-compat display
            CoverUrl = string.IsNullOrWhiteSpace(candidate.CoverUrl) ? null : candidate.CoverUrl,
            Isbns = candidate.Isbns
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(i => new WishlistItemIsbn { Isbn = i })
                .ToList(),
        };
        db.WishlistItems.Add(item);
        await db.SaveChangesAsync();

        var row = new WishlistRow(
            item.Id, item.Title, item.Author, item.Priority, item.Isbn,
            item.CoverUrl, null, null);

        // Insert at the front of the in-memory list so it shows up
        // immediately without a full reload, then re-sort.
        Wishlist.Insert(0, row);
        Wishlist = Wishlist
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Title)
            .ToList();
        return row;
    }

    // ---- Series gaps ----

    public List<SeriesGap> SeriesGaps { get; private set; } = [];
    public bool GapsLoaded { get; private set; }

    /// <summary>Open-ended series (no `ExpectedCount` set) where the user
    /// owns at least one book. Populated alongside <see cref="SeriesGaps"/>
    /// by <see cref="LoadSeriesGapsAsync"/>. The "Add next N" wishlist
    /// flow on /wishlist uses these — the user picks how many forward
    /// slots to seek since the total length is unknown.</summary>
    public List<OpenSeries> OpenSeriesList { get; private set; } = [];

    public async Task LoadSeriesGapsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Structured series with a known expected count where we're missing works.
        // Series → Works → Books gives us the parent book(s) to link to.
        var incompleteSeries = await db.Series
            .Include(s => s.Works).ThenInclude(w => w.Books)
            .Where(s => s.Type == SeriesType.Series && s.ExpectedCount != null)
            .ToListAsync();

        SeriesGaps = incompleteSeries
            .Select(s =>
            {
                // Only true numbered volumes occupy a slot (shared rule —
                // SeriesSlots.OccupiesNumberedSlot). A floored interquel ("4.5",
                // SeriesOrderDisplay set) must not count as owning slot #4, or
                // the real #4 gap is silently hidden.
                var ownedPositions = s.Works
                    .Where(w => SeriesSlots.OccupiesNumberedSlot(w.SeriesOrder, w.SeriesOrderDisplay))
                    .Select(w => w.SeriesOrder!.Value)
                    .Where(o => o <= s.ExpectedCount!.Value)
                    .ToHashSet();

                var missing = new List<int>();
                for (int i = 1; i <= s.ExpectedCount!.Value; i++)
                {
                    if (!ownedPositions.Contains(i))
                        missing.Add(i);
                }

                return new SeriesGap(
                    s.Id,
                    s.Name,
                    s.Author,
                    ownedPositions.Count,
                    s.ExpectedCount.Value,
                    missing,
                    s.Works.OrderBy(w => w.SeriesOrder ?? int.MaxValue)
                        .Select(w => new OwnedSeriesBook(
                            w.Books.FirstOrDefault()?.Id ?? 0,
                            w.Title,
                            SeriesOrderParser.Format(w.SeriesOrder, w.SeriesOrderDisplay)))
                        .ToList());
            })
            .Where(g => g.MissingPositions.Count > 0)
            .OrderBy(g => g.SeriesName)
            .ToList();

        // Open-ended series — no ExpectedCount, but the user owns at
        // least one numbered book. Drive the "add next N missing" flow
        // on /wishlist. Highest-owned-order seeds the suggestion ("you
        // own up to #7, add the next 10?"); series with all-null
        // SeriesOrder still surface (HighestOwnedOrder=0) so the user
        // can mark the first 10 sought from scratch.
        var openSeries = await db.Series
            .Include(s => s.Works)
            .Where(s => s.Type == SeriesType.Series && s.ExpectedCount == null && s.Works.Any())
            .ToListAsync();

        OpenSeriesList = openSeries
            .OrderBy(s => s.Name)
            .Select(s =>
            {
                var orders = s.Works
                    .Where(w => w.SeriesOrder.HasValue)
                    .Select(w => w.SeriesOrder!.Value)
                    .OrderBy(n => n)
                    .ToList();
                return new OpenSeries(
                    s.Id,
                    s.Name,
                    s.Author,
                    s.Works.Count,
                    orders.Count == 0 ? 0 : orders.Max(),
                    orders);
            })
            .ToList();

        GapsLoaded = true;
    }

    /// <summary>Bulk-add wishlist stubs for missing slots in a series.
    /// One <see cref="WishlistItem"/> per requested slot, titled
    /// <c>"{SeriesName} #{slot}"</c> (placeholder — user enriches later
    /// once they know which volume they're chasing) and authored as the
    /// series's display author. SeriesId + SeriesOrder are set so the
    /// row renders with the series badge and lines up against gap
    /// detection. Existing wishlist rows at the same (SeriesId,
    /// SeriesOrder) are skipped silently to make re-runs idempotent —
    /// asking for slots 4–7 twice still ends up with one row per slot.
    /// Returns the count actually added (excluding the silent skips).</summary>
    public async Task<int> AddSeriesSlotsToWishlistAsync(int seriesId, IReadOnlyList<int> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var deduped = slots
            .Where(s => s > 0)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (deduped.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync();

        var series = await db.Series.FindAsync(seriesId);
        if (series is null)
        {
            logger.LogWarning("AddSeriesSlotsToWishlist called with unknown SeriesId {SeriesId}", seriesId);
            return 0;
        }

        // Existing wishlist rows for this series + the requested slots —
        // load in one query so the dedup pass doesn't N+1.
        var alreadyWishlisted = await db.WishlistItems
            .Where(w => w.SeriesId == seriesId && w.SeriesOrder != null && deduped.Contains(w.SeriesOrder!.Value))
            .Select(w => w.SeriesOrder!.Value)
            .ToListAsync();
        var alreadySet = alreadyWishlisted.ToHashSet();

        var author = string.IsNullOrWhiteSpace(series.Author) ? "Unknown" : series.Author.Trim();
        var added = 0;
        foreach (var slot in deduped)
        {
            if (alreadySet.Contains(slot)) continue;
            db.WishlistItems.Add(new WishlistItem
            {
                Title = $"{series.Name} #{slot}",
                Author = author,
                Priority = WishlistPriority.Medium,
                SeriesId = seriesId,
                SeriesOrder = slot,
            });
            added++;
        }

        if (added == 0) return 0;

        await db.SaveChangesAsync();

        // If the wishlist is already loaded, refresh it so the new
        // stubs surface immediately. Skip if not loaded — the user
        // hasn't asked to see the list yet.
        if (WishlistLoaded) await LoadWishlistAsync();

        return added;
    }

    // ---- Wishlist management ----

    public List<WishlistRow> Wishlist { get; private set; } = [];
    public bool WishlistLoaded { get; private set; }
    public bool ShowingQuickAdd { get; set; }
    public QuickAddInput QuickAdd { get; set; } = new();

    public async Task LoadWishlistAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        Wishlist = await db.WishlistItems
            .Include(w => w.Series)
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.Title)
            .Select(w => new WishlistRow(
                w.Id, w.Title, w.Author, w.Priority, w.Isbn,
                w.CoverUrl,
                w.Series != null ? w.Series.Name : null,
                w.SeriesOrder))
            .ToListAsync();
        WishlistLoaded = true;
    }

    public async Task AddManualAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickAdd.Title)) return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var isbn = string.IsNullOrWhiteSpace(QuickAdd.Isbn) ? null : QuickAdd.Isbn.Trim();
        var item = new WishlistItem
        {
            Title = QuickAdd.Title.Trim(),
            Author = string.IsNullOrWhiteSpace(QuickAdd.Author) ? "Unknown" : QuickAdd.Author.Trim(),
            Priority = QuickAdd.Priority,
            Isbn = isbn,
            // Mirror the typed ISBN into the new table so PR D's scan-flag
            // catches QuickAdd-entered wishlist hits the same way it
            // catches search-and-add ones.
            Isbns = isbn is null ? [] : [new WishlistItemIsbn { Isbn = isbn }],
        };

        db.WishlistItems.Add(item);
        await db.SaveChangesAsync();

        Wishlist.Insert(0, new WishlistRow(
            item.Id, item.Title, item.Author, item.Priority, item.Isbn, null, null, null));
        Wishlist = Wishlist
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Title)
            .ToList();

        QuickAdd = new();
        ShowingQuickAdd = false;
    }

    public async Task RemoveFromWishlistAsync(int itemId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.WishlistItems.FindAsync(itemId);
        if (item is not null)
        {
            db.WishlistItems.Remove(item);
            await db.SaveChangesAsync();
        }
        Wishlist.RemoveAll(i => i.Id == itemId);
    }

    /// <summary>
    /// Marks a wishlist item as "bought" — creates a Book + Edition + Copy
    /// with follow-up tag and default values, then removes the wishlist item.
    /// Returns the new book ID for navigation.
    /// </summary>
    public async Task<int?> MarkAsBoughtAsync(WishlistRow item)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var followUpTag = await db.Tags.FirstOrDefaultAsync(t => t.Name == "follow-up");
        if (followUpTag is null)
        {
            followUpTag = new Tag { Name = "follow-up" };
            db.Tags.Add(followUpTag);
        }

        var author = await AuthorResolver.FindOrCreateAsync(item.Author, db);
        var work = new Work { Title = item.Title };
        AuthorResolver.AssignAuthors(work, [author]);
        var book = new Book
        {
            Title = item.Title,
            Tags = [followUpTag],
            Editions = [],
            Works = [work]
        };

        if (!string.IsNullOrWhiteSpace(item.Isbn))
        {
            book.Editions.Add(new Edition
            {
                Isbn = item.Isbn,
                Format = BookFormat.TradePaperback,
                Copies = [new Copy { Condition = BookCondition.Good }]
            });
        }

        db.Books.Add(book);

        var wishlistItem = await db.WishlistItems.FindAsync(item.Id);
        if (wishlistItem is not null)
            db.WishlistItems.Remove(wishlistItem);

        await db.SaveChangesAsync();

        Wishlist.RemoveAll(i => i.Id == item.Id);
        return book.Id;
    }

    public static string PriorityBadgeClass(WishlistPriority p) => p switch
    {
        WishlistPriority.High => "bg-danger",
        WishlistPriority.Medium => "bg-warning text-dark",
        WishlistPriority.Low => "bg-secondary",
        _ => "bg-light text-dark"
    };

    public class QuickAddInput
    {
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Isbn { get; set; }
        public WishlistPriority Priority { get; set; } = WishlistPriority.Medium;
    }

    public record WishlistRow(
        int Id, string Title, string Author, WishlistPriority Priority,
        string? Isbn, string? CoverUrl, string? SeriesName, int? SeriesOrder);

    /// <summary>Unified shape for search candidates from both the ISBN
    /// lookup (BookLookupResult) and the title/author search
    /// (BookSearchCandidate). Carries enough metadata to populate a
    /// WishlistItem on Add without re-querying.
    ///
    /// AlreadyOwnedBookId / AlreadyWishlistedItemId surface duplicate
    /// matches from the ISBN search path so the UI can warn before
    /// the user clicks Add. Both default null for text-search candidates
    /// (no ISBN to match against) and for ISBN candidates that don't
    /// duplicate anything.</summary>
    public record WishlistCandidate(
        string? Title,
        string? Author,
        IReadOnlyList<string> Isbns,
        string? CoverUrl,
        string Source,
        int? AlreadyOwnedBookId = null,
        int? AlreadyWishlistedItemId = null);

    public record SeriesGap(
        int SeriesId, string SeriesName, string? Author,
        int OwnedCount, int ExpectedCount,
        List<int> MissingPositions,
        List<OwnedSeriesBook> OwnedBooks);

    public record OwnedSeriesBook(int Id, string Title, string? SeriesOrderLabel);

    /// <summary>Series with no ExpectedCount where the user owns at least
    /// one Work. HighestOwnedOrder seeds the "Add next N missing" flow
    /// (0 when no Works carry a SeriesOrder yet — UI suggests starting
    /// from #1). OwnedOrders is the full set so the suggestion can skip
    /// slots the user already owns when computing the next-N range.</summary>
    public record OpenSeries(
        int SeriesId,
        string SeriesName,
        string? Author,
        int OwnedCount,
        int HighestOwnedOrder,
        IReadOnlyList<int> OwnedOrders);
}
