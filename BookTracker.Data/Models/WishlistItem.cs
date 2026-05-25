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
