# Bookshop mode — design

A mobile-optimised, offline-capable lookup surface at `/bookshop`. Solves the killer mobile use cases — ISBN-scan-while-shopping and author-lookup-on-the-go — with sub-100ms responsiveness independent of network state.

This doc is the design reference for `/bookshop`.

## Why this matters

Four motivations, calibrated against the actual use:

1. **Practical** — bookshop scanning in low-signal areas. Cellular drops to nothing inside large bookshops with concrete walls + steel shelving; the Blazor Server app stops working at all in that state.
2. **UX joy** — the "did I click that?" pattern + the lock-screen-recover lag are the dominant friction in routine mobile use. Even when online, the SignalR roundtrip floor on cellular sits at hundreds of ms; a local-cache lookup is sub-10ms.
3. **Demo without apologies** — "just give it a minute…" is the wrong introduction when showing the app to a person who doesn't yet know what it does.
4. **Portfolio / employer demo** — the project's narrative is "AI-collaborative engineering." A laggy demo undercuts the narrative; a snappy demo reinforces it.

(2) – (4) all push the same direction — **the lookup paths must feel native-app fast**. (1) extends "fast" to "available offline."

## Acceptance criteria

The bookshop mode meets its bar when:

- **Cold open on mobile** (no SignalR connection, possibly no network at all): `/bookshop` route loads from cache, renders an interactive scan surface in <1s.
- **ISBN scan answers in <200ms**: tap "Scan", scan succeeds, result card visible, regardless of network state.
- **Author lookup answers in <200ms**: tap "Author lookup", type 3+ characters, see matching authors with their book counts.
- **Catalog snapshot stays fresh**: when online, the cache silently refreshes if older than 24h; manual "Refresh now" button always available.
- **Stale catalog is honest**: if the cache is >7 days old, the page warns the user before relying on it.
- **Open-in-app links work online**: tapping a result navigates to `/books/{id}` (the live, edit-capable detail page), gracefully degrading to "you'll need to be online for this" when offline.
- **No regressions** for existing Blazor Server pages — the SW changes don't break the existing PWA caching for static assets or `/_blazor/*`.

## Architecture overview

Three layers, none of which require an active SignalR circuit:

1. **`/bookshop` route** — Razor page with no Blazor interactivity (the app's global InteractiveServer rendermode still opens a circuit, but the page doesn't use it). The HTML is plain markup; interactivity is pure JavaScript reading from IndexedDB. Auth-gated by Easy Auth at first visit; SW caches the rendered HTML for offline access thereafter.

2. **Service worker** — `wwwroot/service-worker.js` applies **route-specific caching strategies**. Cache-first for `/bookshop*` and `/api/catalog-snapshot`; network-first-with-cache-fallback for everything else.

3. **IndexedDB catalog cache** — stores a slim snapshot of the catalog: books (id, title, primary author, all authors, status, rating, ISBNs), authors (id, name, canonical id, book ids). Indexed for ISBN lookup (multi-entry on `isbns`) and primary-author lookup. Module at `wwwroot/js/catalog-cache.js` exposes `init()`, `lookupByIsbn()`, `lookupByAuthor()`, `searchAuthors()`, `refresh()`, `getMeta()`. Stores: `books` (key=id, multiEntry index on `isbns`, index on `primaryAuthor`, the author's name), `authors` (key=id, index on `canonicalId`), `meta` (key/value for sync timestamp, version).

The page's JS reads from IndexedDB on `DOMContentLoaded`, wires up event handlers in plain JS, and does not depend on Blazor's circuit being alive. Server-side methods on the Razor page are reachable when online (e.g. for "open in app" links) but not required for the offline lookup paths.

## Catalog snapshot

The endpoint `GET /api/catalog-snapshot` returns the slim catalog. Easy-Auth-gated like the rest of the app; SW caches the response.

### Data shape

The shape is the `CatalogSnapshot` record family in `BookTracker.Shared/Catalog/` (see `ARCHITECTURE.md` › GetCatalogSnapshot), shared with Bookshelf. `/bookshop` reads the books (title, authors, status, rating, ISBNs) and the authors (name, canonical id, book count) from it.

### Size estimate at the 3000+ books target

| Layer | Per-row | Rows | Total |
|---|---|---|---|
| Books | ~150 bytes JSON | 3,000 | ~450 KB |
| Authors | ~60 bytes JSON | ~500 | ~30 KB |
| Total raw | | | **~480 KB** |
| After gzip | | | **~120-180 KB** |

Comfortably within service-worker pre-cache range. A single network call. IndexedDB write on first sync takes <1s on a modern phone.

### What's NOT in the snapshot (deliberate)

- **Notes, copy details.** Edit-flow data. Bookshop mode is read-only-lookup; full detail is reachable via the "Open in app" deep-link.
- **Tags and genres.** `CatalogSnapshot` carries neither; they are browsing facets for the full app, not lookup data.
- **Dismissal state for duplicates.** Edit-flow domain.

## Sync model

Cache-first with background refresh:

- **First visit (online)**: SW pre-caches the page HTML + the catalog snapshot. ~200 KB transfer; ~1s on cellular.
- **Subsequent visits (online)**: page renders instantly from cache; JS checks `meta.syncedAt`; if >24h old, kicks off a background `refresh()` (no UI block). User sees yesterday's data for ~1s while fresh data syncs.
- **Subsequent visits (offline)**: page renders from cache; lookups answer from IndexedDB. Footer shows "Synced N hours ago — offline" so the user knows they're operating on cached data.
- **Stale beyond 7 days**: footer warning escalates to "Catalog last synced N days ago. Refresh when online." The 7-day threshold is intentionally lenient — bookshop scanning is mostly informational; even a week-old "do I have this?" answer is correct for ~99% of books in the library.
- **Manual refresh**: always available. "Refresh now" button in the footer; spinner; success/failure snackbar.

## /bookshop UI

Mobile-first single-column layout. Two modes via a top tab switcher (Scan / Author).

### Scan mode (default)

```
[ ╔═════════════════════╗ ]
[ ║                     ║ ]
[ ║   📷  Scan ISBN     ║ ]
[ ║                     ║ ]
[ ╚═════════════════════╝ ]

[ Or enter ISBN manually:   ]
[ [____________] [Lookup]   ]

[ Last result:               ]
[ ┌─────────────────────────┐]
[ │ Rendezvous with Rama    │]
[ │ Arthur C. Clarke        │]
[ │ ★★★★★  Read              │]
[ │ [Open in app →]         │]
[ └─────────────────────────┘]
```

When ISBN is NOT in catalog: the result card says "Not in your library" with two action buttons:
- **Add to wishlist** (requires online — surfaces the existing `/shopping` add path)
- **Add now** (requires online — deep-link into `/books/add` pre-filled with the scanned ISBN)

### Author lookup mode

```
[ Search authors:            ]
[ [_____________________ 🔍 ]]

[ ┌─────────────────────────┐]
[ │ Arthur C. Clarke   (5)  │]
[ │ Asimov, Isaac      (12) │]
[ │ Atwood, Margaret   (3)  │]
[ └─────────────────────────┘]

[ Tap an author → list their books ]
```

After tap-author:

```
[ ← Authors                  ]
[                            ]
[ Arthur C. Clarke           ]
[ ┌─────────────────────────┐]
[ │ 2001: A Space Odyssey   │]
[ │ ★★★★  Read               │]
[ │ [Open in app →]         │]
[ └─────────────────────────┘]
[ ┌─────────────────────────┐]
[ │ Childhood's End         │]
[ │ Unread                  │]
[ │ [Open in app →]         │]
[ └─────────────────────────┘]
```

### Footer

Always-visible thin footer:

```
[ Catalog: 3,000 books · Synced 2h ago · [Refresh] ]
```

When stale (>7 days) or offline:

```
[ ⚠ Synced 9 days ago — refresh when online · [Refresh] ]
[ 📵 Offline — showing cached data                      ]
```

Offline indicator uses `navigator.onLine` + visibilitychange events. Unauthenticated cold-load works — the SW serves cached HTML and the JS reads cached data; auth is only required for fresh syncs and "Open in app."

## Open decisions

- **Result card cover image** — a placeholder icon, or a tiny thumbnail per book from the snapshot (~50 bytes per book × 3000 = 150 KB extra)? Icon only unless mobile testing shows the icon-only card feels too anonymous.
- **Service worker BackgroundSync API** — Chrome supports it; iOS doesn't. The page refreshes on visit; if actual usage shows a daily background refresh while the phone is in a pocket is wanted, revisit.
- **Author canonical-vs-alias UI** — when search hits both Stephen King and Richard Bachman, surface only the canonical, or both? Leaning canonical-only in the result list, with alias awareness through the search itself (typing "Bachman" matches King's row).

## What this is explicitly NOT

- **Not a full WebAssembly Blazor migration.** Reuses Blazor Server for everything except `/bookshop`. The static-SSR + JS approach for one route doesn't propagate.
- **Not a sync-back / offline-write model.** Bookshop mode is read-only-lookup. Adds, edits, status changes all require an online round-trip via the existing edit flows. (v2 could explore "queue offline adds and replay when online," but the use case for that is much narrower than the lookup case.)
- **Not a replacement for the existing PWA install.** The whole BookTracker app stays installable as one PWA; `/bookshop` is just the route within it that's offline-first.
- **Not a generic offline-first refactor of every page.** Only the lookup paths (ISBN check, author search) need offline. Edit pages are deliberately online-only — adding a book is a network-required event by design.

## Cross-references

- The mobile-latency root-cause analysis lives in [the May 8 perf-investigation blog post](../blog/2026-05-08-01-i-blamed-the-cold-start-the-trace-disagreed.md). The "Postscript: the deploy itself was the incident" section + the postscript-meta-lesson section frame why static-pattern fixes alone won't hit the demo-quality bar.
- The existing PWA caching strategy (network-first with cache fallback for static assets, `/_blazor/*` passthrough) is documented in `ARCHITECTURE.md` "Progressive Web App (PWA)" section. The bookshop SW rules layer on top, not over.
