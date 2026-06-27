using BookTracker.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTracker.Application.Books;

// Shared "best-effort cover" pick for the merge-preview cards. Both the Author-
// and Work-merge previews want the DefaultCoverArtUrl of a candidate Book,
// preferring one that contains a single Work (its cover represents that Work
// faithfully) over a compendium, and falling back to any candidate. Callers pass
// their own candidate set (books credited to an author / books containing a
// work); this owns the single-Work-else-any rule that was copied between the two
// previews (TD-16).
public static class BookCovers
{
    public static async Task<string?> PickAsync(IQueryable<Book> candidates, CancellationToken ct)
    {
        // Prefer a single-Work book's cover. Faithful to the original: this takes
        // the first single-Work book's cover even if it's null, and only then
        // falls back to any candidate — so a single-Work book with no cover still
        // defers to a covered compendium.
        var singleWorkCover = await candidates
            .Where(b => b.Works.Count == 1)
            .Select(b => b.DefaultCoverArtUrl)
            .FirstOrDefaultAsync(ct);

        return singleWorkCover ?? await candidates
            .Select(b => b.DefaultCoverArtUrl)
            .FirstOrDefaultAsync(ct);
    }
}
