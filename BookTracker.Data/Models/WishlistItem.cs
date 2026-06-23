using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BookTracker.Data.Models;

public enum WishlistPriority
{
    Low,
    Medium,
    High
}

public class WishlistItem
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    public WishlistPriority Priority { get; set; } = WishlistPriority.Medium;

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Price { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    /// <summary>Legacy single-ISBN column from the original /shopping
    /// wishlist shape. New entries (post-2026-05-25 search-and-add) write
    /// to <see cref="Isbns"/> instead, but this column stays for back-compat
    /// with rows captured via the previous QuickAdd UI.</summary>
    [MaxLength(20)]
    public string? Isbn { get; set; }

    /// <summary>Every known ISBN for this wishlisted book. Search-and-add
    /// populates from the BookLookupService result (Open Library / Google
    /// Books / Trove can each surface different ISBNs for the same work).
    /// Backs the PR D scan-flag lookup — when Drew scans a book in a
    /// bookshop, ANY of these ISBNs matching means "on your wishlist".</summary>
    public List<WishlistItemIsbn> Isbns { get; set; } = [];

    /// <summary>Upstream cover URL captured at add time. Lightweight
    /// (not mirrored to blob storage like Book/Edition covers — wishlist
    /// covers are best-effort thumbnails, refresh as upstream URLs
    /// rotate is acceptable). Null when the search candidate had no
    /// cover or for legacy QuickAdd rows.</summary>
    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    public int? SeriesId { get; set; }
    public Series? Series { get; set; }

    /// <summary>Position in the series this item would fill.</summary>
    public int? SeriesOrder { get; set; }

    // --- Aggregate behaviour -------------------------------------------------
    // WishlistItem is a small aggregate. Its one non-obvious invariant is the
    // ISBN dual-write: the legacy single-column Isbn (back-compat) must stay the
    // primary of the Isbns table, and the table must be de-duplicated. SetIsbns
    // is the single place that rule lives — both add paths route through it.
    // Author is normalised to a non-blank value ("Unknown" fallback) since the
    // column is Required. See docs/BACKEND-REFACTOR-DESIGN.md.

    /// <summary>Creates a wishlist item from a search candidate or a quick-add:
    /// title (required), author (blank → "Unknown"), priority, the known ISBNs
    /// (dual-written via <see cref="SetIsbns"/>), and an optional cover URL.</summary>
    public static WishlistItem Create(
        string? title, string? author, WishlistPriority priority,
        IEnumerable<string> isbns, string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainRuleException("A wishlist item needs a title.");

        var item = new WishlistItem
        {
            Title = title.Trim(),
            Author = string.IsNullOrWhiteSpace(author) ? "Unknown" : author.Trim(),
            Priority = priority,
            CoverUrl = coverUrl.TrimToNull(),
        };
        item.SetIsbns(isbns);
        return item;
    }

    /// <summary>Creates a placeholder stub for a missing numbered series slot —
    /// titled "{series} #{slot}", authored as the series' display author, linked
    /// to the series + slot so it renders with the badge and lines up against
    /// gap detection. No ISBNs or cover (the user enriches it later).</summary>
    public static WishlistItem CreateSeriesSlot(int seriesId, string seriesName, int slot, string? seriesAuthor)
    {
        return new WishlistItem
        {
            Title = $"{seriesName} #{slot}",
            Author = string.IsNullOrWhiteSpace(seriesAuthor) ? "Unknown" : seriesAuthor.Trim(),
            Priority = WishlistPriority.Medium,
            SeriesId = seriesId,
            SeriesOrder = slot,
        };
    }

    /// <summary>Replaces the known-ISBN set, enforcing the dual-write: the legacy
    /// <see cref="Isbn"/> column becomes the primary (first non-blank), and
    /// <see cref="Isbns"/> holds the trimmed, case-insensitively de-duplicated
    /// list. Empty input clears both.</summary>
    public void SetIsbns(IEnumerable<string> isbns)
    {
        var clean = (isbns ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Isbn = clean.FirstOrDefault();   // legacy column — primary ISBN for back-compat
        Isbns = clean.Select(i => new WishlistItemIsbn { Isbn = i }).ToList();
    }
}

/// <summary>One row per ISBN known for a wishlisted book. Many-to-one
/// with WishlistItem; cascade deletes when the parent goes. Indexed on
/// Isbn so the PR D scan-flag lookup is a B-tree seek.</summary>
public class WishlistItemIsbn
{
    public int Id { get; set; }

    public int WishlistItemId { get; set; }
    public WishlistItem WishlistItem { get; set; } = null!;

    [Required, MaxLength(20)]
    public string Isbn { get; set; } = string.Empty;
}
