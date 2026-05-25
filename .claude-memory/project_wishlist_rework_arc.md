---
name: wishlist-rework-arc
description: Four-PR arc reshaping the original /shopping surface into a clearer split — in-shop consumption stays on bookshelf / /bookshop; capture / wishlist management lives on bookcase web. Each PR sequenced in order with the explicit decisions Drew named 2026-05-25 baked in.
metadata: 
  node_type: memory
  type: project
  originSessionId: 0613aa7d-1b0d-4ae3-ae9e-2227b8cabcd7
---

Drew's framing 2026-05-25 (browsing prompted him to realise "what am I looking for" wasn't surfaced on Bookshelf): consolidate the in-shop concerns into bookshelf, and rework what's on Bookcase web into a *capture* surface for proactively managing the want list.

**Why:** `/shopping` today mixes three things — "do I have this?", series gaps, wishlist — none of which are visible on Bookshelf where Drew is when the questions actually bite (in a bookshop). Two surfaces, two concerns: bookcase = at-home capture, bookshelf = on-the-go consumption. The split also unblocks TODO #48 (Bookshelf wishlist surface — backend already shipped) which had been waiting on a planning trigger.

**How to apply:** When Drew opens a "what's next" conversation, this arc is the active queue. Pick from the top — sequential shipping, not parallel.

## PR A — Rename `/shopping` → `/wishlist` + drop "Do I have this?" — **SHIPPED 2026-05-25**

Prequel cleanup. Card 1 was a duplicate of /bookshop's scan + Bookshelf ScanPage. `/shopping` kept as a secondary `@page` directive so old bookmarks survive. ShoppingViewModel rename + dead-code removal deferred to PR B (touches the VM anyway). Branch: `feat/wishlist-rename-and-drop-card-1`.

## PR B — Search-and-add to wishlist — **SHIPPED 2026-05-25**

Single PR (`feat/wishlist-search-and-add` → #303). `/wishlist` got the search-and-add card at the top — ISBN-shaped queries route to `LookupByIsbnAsync`, text queries route to `SearchByTitleAuthorAsync`. New `WishlistCandidate` record unifies both shapes. `AddCandidateAsync` persists Title + Author + CoverUrl + ISBNs (both legacy single column + new `WishlistItemIsbn` table). `ShoppingViewModel` → `WishlistViewModel` rename + dead-Card-1 code removal landed in the same PR. Plus three in-PR tweaks based on Drew's testing-feedback while reviewing:
- ISBN-search duplicate-detection (warning badges on candidate card when book already in library or already wishlisted)
- Advanced-search expander (Title / Author / ISBN as separate fields) — addresses Open Library's title-vs-author fuzzy match
- BookLookupService switched to Lucene `q=author:"phrase"` instead of `?author=value` URL param — Open Library's `?author=` does loose word-by-word matching, the `q=` route gives phrase semantics

Migration `WishlistCoverAndMultiIsbn` adds `WishlistItem.CoverUrl` + `WishlistItemIsbn` table. Locked design call confirmed: separate table, not comma-joined.

## PR C — Series-driven wishlist additions — **SHIPPED 2026-05-25**

Single PR (`feat/wishlist-series-driven-additions`). Two flows on /wishlist driven off the existing series-gap detection.

**Finite series** (`ExpectedCount` set): missing-position badges on the Series gaps card became clickable selectors — tap `#N` to toggle (outlined → filled `bg-primary`), then "Add N to wishlist" per series row creates one stub per slot. Select-all / Clear affordances.

**Open-ended series** (`ExpectedCount` null, ≥1 owned book): new card below. Per series: "{name} ({author}) — you own N (highest #M). Add the next [10] missing slots starting from #(M+1)" with numeric input (default 10 per the locked design call; range 1–100). Forward-only — no gap-filling below highest owned (kept the next-N compute simple; can revisit if Drew finds he wants gap-fill).

Stubs each carry `Title="{SeriesName} #{order}"` + `Author = Series.Author ?? "Unknown"` + `SeriesId` + `SeriesOrder`. Dedup by `(SeriesId, SeriesOrder)` skips already-wishlisted slots silently — re-runs are idempotent. User enriches title + cover later from the PR B search-and-add card.

VM additions: `AddSeriesSlotsToWishlistAsync(seriesId, slots) → int addedCount`; `LoadSeriesGapsAsync` extended to also populate `OpenSeriesList: List<OpenSeries>`. 6 new VM tests. 488/488 main + 79/79 cache.

## PR D — Bookshelf wishlist surface + scan-flag — **SHIPPED 2026-05-25**

Single PR (`feat/wishlist-bookshelf-and-scan-flag`). Closes TODO #48. Three pieces:

- **Snapshot DTO + service extension** — `WishlistItemSnapshot` gained `CoverUrl: string?` + `Isbns: IReadOnlyList<string>?`. `WishlistSnapshotService` projects `CoverUrl` directly and unions the legacy `WishlistItem.Isbn` single column with the per-row `WishlistItemIsbn` rows (PR B), deduped case-insensitively. Legacy rows (pre-PR-B) still surface their ISBN in the flat `Isbns` list — scan-flag covers them without a data migration.
- **Mobile cache + ApiClient** — three new tables (`CachedWishlistItem`, `CachedWishlistItemIsbn`, `WishlistBoughtLocal`); five new `ICatalogCache` methods (`PopulateWishlistAsync`, `GetWishlistAsync`, `MarkBoughtLocallyAsync`, `UnmarkBoughtLocallyAsync`, `IsWishlistedIsbnAsync`). Bought-local entries survive catalog refresh — orphan-tolerant. `ApiClient.GetWishlistSnapshotAsync` mirrors the catalog path.
- **Bookshelf MAUI surface** — new `WishlistPage` (cover thumbnail + title/author/priority badge + Bought button per row), MainPage `WishlistButton` (brass in-bookshop CTA), and `ScanPage.WishlistFlag` border (visible above FoundFrame / MissingFrame when scanned ISBN is wishlisted). Web `/bookshop` wishlist tab dropped from scope — Drew can revisit if it surfaces.

12 new tests (10 cache + 2 snapshot service). 490/490 main + 89/89 cache.

## Arc closed — **RETIRE AFTER NEXT SYNC**

All four PRs shipped. `/shopping` is `/wishlist` on the Web; Bookshelf surfaces the wishlist offline; scan-flag answers "am I looking for this?" in real time. The deferred items below remain open for their own conversations.

## Deferred from this arc (explicitly out of scope)

- **Book-level Editor model** — separate from this arc; held under TODO #51's "full editor model" sub-row, still needs its own planning session.
- **Auto-resolve wishlist hit → Book on save.** When a wishlisted ISBN is later scanned + added via Add Book, the wishlist row could auto-resolve as "bought" rather than needing a manual mark. Tempting but cross-cutting; defer until Drew has the surface in real use.
- **Web `/bookshop` wishlist tab** (TODO #28 sub-piece (c)) — out of PR D scope; the Bookshelf MAUI surface covers the in-shop case. Revisit if Drew finds he wants the same view on the PWA too.
