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

## PR B — Search-and-add to wishlist (M, next up)

Richest single win. New search box on `/wishlist` that reuses `BookLookupService` (the same ISBN/title/author lookup powering Add Book). Pick-from-candidates UX. Wishlist row captures Title, Author, all known ISBNs, and CoverUrl — lightweight, not a full Book/Work entity, just enough metadata to recognise the same book when scanned later on Bookshelf.

**Schema additions** (locked 2026-05-25):
- `WishlistItem.CoverUrl: string? (MaxLength 500)`
- **Separate `WishlistItemIsbn` table** (not comma-joined) — mirrors `Edition.Isbn` shape, one row per ISBN, clean query path for the scan-flag lookup that PR D will need
- One migration adding both

**Out of scope:** the existing `WishlistItem.Isbn` (single column) stays for back-compat; PR B writes new entries to the multi-ISBN table only.

## PR C — Series-driven wishlist additions (M)

From a Series detail page (or a new tab on /wishlist), "Mark this series as sought" view. Lists missing slots from gap detection. Two flows:

- **Finite series (`ExpectedCount` set):** checkbox-grid of missing slots; "Add selected" creates one `WishlistItem` per slot
- **Infinite / no ExpectedCount:** offer "Add next N missing" with **N=10 default** (Drew's call 2026-05-25 — round number, low friction, can re-run for more)

The series-add path doesn't have title data for unowned slots without an upstream lookup. v1 approach: capture as stubs ("Foundation #4 — unknown title") and let Drew enrich later from the wishlist surface. Optional follow-on: fire OpenLibrary search-by-series-slot to pre-fill.

## PR D — Bookshelf wishlist surface + scan-flag (M)

Closes TODO #48 (backend `/api/wishlist-snapshot` already shipped 2026-05-13 — verified during the 2026-05-24 TODO sync). Two pieces:

- New `WishlistPage` on Bookshelf MAUI — list view backed by `/api/wishlist-snapshot`. Local SQLite `WishlistBoughtLocal` table for the "bought" toggle (self-heals on next catalog refresh: server-side wishlist row gone = local-bought entry becomes a no-op).
- **ScanPage scan-flag** — when the scanned ISBN matches any wishlisted ISBN (looked up via the new `WishlistItemIsbn` table from PR B), the result card highlights with "On your wishlist" badge.

Optional in same PR: parallel wishlist tab on Web `/bookshop` (the sub-piece (c) from the original TODO #28 plan).

## Deferred from this arc (explicitly out of scope)

- **Book-level Editor model** — separate from this arc; held under TODO #51's "full editor model" sub-row, still needs its own planning session.
- **Auto-resolve wishlist hit → Book on save.** When a wishlisted ISBN is later scanned + added via Add Book, the wishlist row could auto-resolve as "bought" rather than needing a manual mark. Tempting but cross-cutting; defer to its own decision after PR D ships and Drew has the surface in real use.

## What this memory is NOT

Not a list of all open work — TODO.md is the master. This memory is just the load-bearing PR ordering + the design calls Drew named upfront (`N=10` default, separate `WishlistItemIsbn` table, etc.) so the next session picks up where this one left off.

Retire after PR D ships.
