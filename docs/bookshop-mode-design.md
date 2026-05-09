# Bookshop mode — design

A mobile-optimised, offline-capable lookup surface at `/bookshop`. Solves the killer mobile use cases — ISBN-scan-while-shopping and author-lookup-on-the-go — with sub-100ms responsiveness independent of network state.

This doc covers the whole arc as a reference for the implementation PRs that follow. PR breakdown at the end.

## Why this matters

Four motivations, calibrated against the actual use:

1. **Practical** — bookshop scanning in low-signal areas. Cellular drops to nothing inside large bookshops with concrete walls + steel shelving; the current Blazor Server app stops working at all in that state.
2. **UX joy** — the "did I click that?" pattern + the lock-screen-recover lag are the dominant friction in routine mobile use. Even when online, the SignalR roundtrip floor on cellular sits at hundreds of ms; a local-cache lookup is sub-10ms.
3. **Demo without apologies** — "just give it a minute…" is the wrong introduction when showing the app to a person who doesn't yet know what it does.
4. **Portfolio / employer demo** — the project's narrative is "AI-collaborative engineering." A laggy demo undercuts the narrative; a snappy demo reinforces it.

(2) – (4) all push the same direction — **the lookup paths must feel native-app fast**. (1) extends "fast" to "available offline."

## Acceptance criteria for v1

The bookshop mode feels done when:

- **Cold open on mobile** (no SignalR connection, possibly no network at all): `/bookshop` route loads from cache, renders an interactive scan surface in <1s.
- **ISBN scan answers in <200ms**: tap "Scan", scan succeeds, result card visible, regardless of network state.
- **Author lookup answers in <200ms**: tap "Author lookup", type 3+ characters, see matching authors with their book counts.
- **Catalog snapshot stays fresh**: when online, the cache silently refreshes if older than 24h; manual "Refresh now" button always available.
- **Stale catalog is honest**: if the cache is >7 days old, the page warns the user before relying on it.
- **Open-in-app links work online**: tapping a result navigates to `/books/{id}` (the live, edit-capable detail page), gracefully degrading to "you'll need to be online for this" when offline.
- **No regressions** for existing Blazor Server pages — the SW changes don't break the existing PWA caching for static assets or `/_blazor/*`.

## Architecture overview

Three layers, none of which require an active SignalR circuit:

1. **`/bookshop` route** — Razor page rendered in **Static SSR** mode (no Interactive Server). The HTML is plain markup; interactivity is pure JavaScript reading from IndexedDB. Auth-gated by Easy Auth at first visit; SW caches the rendered HTML for offline access thereafter.

2. **Service worker** — extends the existing `wwwroot/service-worker.js` with **route-specific caching strategies**. Cache-first for `/bookshop*` and `/api/catalog-snapshot`; network-first-with-cache-fallback for everything else (existing behaviour preserved).

3. **IndexedDB catalog cache** — stores a slim snapshot of the catalog: books (id, title, primary author, all authors, status, rating, ISBNs), authors (id, name, canonical id, book ids). Indexed for ISBN lookup (multi-entry on `isbns`) and primary-author lookup. Module at `wwwroot/js/catalog-cache.js` exposes `init()`, `lookupByIsbn()`, `lookupByAuthor()`, `searchAuthors()`, `refresh()`, `getMeta()`.

The page's JS reads from IndexedDB on `DOMContentLoaded`, wires up event handlers in plain JS, and does not depend on Blazor's circuit being alive. Server-side methods on the Razor page are reachable when online (e.g. for "open in app" links) but not required for the offline lookup paths.

## Catalog snapshot

A new endpoint `GET /api/catalog-snapshot` returns the slim catalog. Easy-Auth-gated like the rest of the app; SW caches the response.

### Data shape

```json
{
  "version": "<git-sha-or-build-id>",
  "syncedAt": "2026-05-09T11:00:00Z",
  "books": [
    {
      "id": 123,
      "title": "Rendezvous with Rama",
      "primaryAuthor": "Arthur C. Clarke",
      "allAuthors": ["Arthur C. Clarke"],
      "status": "Read",
      "rating": 5,
      "isbns": ["9780553287899", "9780575094192"]
    }
  ],
  "authors": [
    {
      "id": 5,
      "name": "Arthur C. Clarke",
      "canonicalId": 5,
      "bookCount": 2
    }
  ]
}
```

### Size estimate at the 3000+ books target

| Layer | Per-row | Rows | Total |
|---|---|---|---|
| Books | ~150 bytes JSON | 3,000 | ~450 KB |
| Authors | ~60 bytes JSON | ~500 | ~30 KB |
| Total raw | | | **~480 KB** |
| After gzip | | | **~120-180 KB** |

Comfortably within service-worker pre-cache range. A single network call. IndexedDB write on first sync takes <1s on a modern phone.

### What's NOT in the snapshot (deliberate)

- **Cover images.** Optional v2 — covers are the bandwidth-heavy part; not needed for "do I have this?" answers. v1 result cards show a generic icon.
- **Notes, tags, edition/copy details.** All edit-flow data. Bookshop mode is read-only-lookup; full detail is reachable via the "Open in app" deep-link.
- **Series / genre data.** Not part of the killer use cases. Add if a v2 use case justifies.
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

## PR breakdown

Five PRs, each shippable independently with user-visible value (or, for the early ones, deployable infrastructure that costs nothing while sitting unused).

### PR 1 — Catalog snapshot API endpoint

- New `GET /api/catalog-snapshot` Minimal API endpoint (or controller) returning the slim catalog JSON.
- Easy Auth gates it like the rest of the app.
- Server-side response caching with a short TTL (e.g. 5 min) so repeated SW pre-cache fetches don't hammer the DB.
- DTO records: `CatalogSnapshot`, `BookSnapshot`, `AuthorSnapshot`. Slim — only fields needed for the lookup paths.
- Tests: snapshot shape, alias rollup correctness, ISBN list per book, response auth-gated.

**Size:** S. Self-contained; nothing else has to change for this to ship. The endpoint sits ready for PR 2 to consume.

### PR 2 — Service worker catalog caching + IndexedDB schema

- Update `wwwroot/service-worker.js` to add route-specific caching strategies. Cache-first for `/bookshop*` and `/api/catalog-snapshot`; preserve existing network-first behaviour everywhere else.
- New `wwwroot/js/catalog-cache.js` module implementing `init()`, `lookupByIsbn(isbn)`, `lookupByAuthor(authorId)`, `searchAuthors(query, limit)`, `refresh()`, `getMeta()`. Pure JS (no Blazor dependency); plays well with both Razor pages and direct browser console testing.
- IndexedDB schema: `books` store (key=id, multiEntry index on `isbns`, index on `primaryAuthorCanonicalId`); `authors` store (key=id, index on `canonicalId`); `meta` store (key/value for sync timestamp, version).
- No UI yet. The SW + JS ship as deployable infrastructure.

**Size:** M. The SW changes need careful testing against the existing PWA flows; IndexedDB schema needs migration handling for future v2 changes. Manual test plan: open DevTools, call `catalogCache.lookupByIsbn(...)`, see results.

### PR 3 — `/bookshop` route + Scan mode

- New `Components/Pages/Bookshop/Index.razor` rendered in Static SSR mode (no Interactive Server).
- Scan mode UI: scan button + manual ISBN entry + result card.
- Reuses existing `BarcodeScanner` JS module (already wired for `/books/add` and `/shopping`).
- Result card supports the "in catalog" + "not in catalog" shapes; "Open in app" deep-links to `/books/{id}`.
- Unauthenticated cold-load works — SW serves cached HTML; JS reads cached data; auth only required for fresh syncs and "Open in app."

**Size:** M. First user-visible delivery. Mobile testing essential — the whole point is the mobile experience.

### PR 4 — Author lookup mode

- Same page, second mode toggle.
- Author search box with debounced input → `catalogCache.searchAuthors(query, limit)` → result list.
- Tap-through to author's books (cached lookup → render list).
- "Open in app" links per book.

**Size:** S. Builds on PR 3's foundations; mostly UI + plumbing.

### PR 5 — Refresh UX, staleness handling, polish

- Footer with sync timestamp + manual refresh button.
- Stale-cache warning at the 7-day threshold.
- Offline indicator (uses `navigator.onLine` + visibilitychange events).
- Empty / error states (e.g. catalog snapshot 401 when auth cookie expired offline).
- Snackbar feedback on refresh success / failure.

**Size:** S. Polish — but the polish IS the demo quality target.

**Total:** ~5 PRs spanning a few sessions. Ship-as-you-go. PR1 is the fastest bootstrap (just a JSON endpoint); PR3 is the user-visible "first feel of bookshop mode" inflection point.

## Open decisions deferred to implementation

These are calls I'd rather make in-PR than pre-commit here:

- **Result card cover image** — show a placeholder icon in v1 (size budget says no), or include a tiny thumbnail URL per book in the snapshot? The latter adds ~50 bytes per book × 3000 = 150 KB extra. Push to v2 unless mobile testing shows the icon-only card feels too anonymous.
- **Service worker BackgroundSync API** — Chrome supports it; iOS doesn't. v1 sticks to "refresh on visit"; if Drew's actual usage shows he wants a daily background refresh while the phone is in his pocket, revisit.
- **Author canonical-vs-alias UI** — when search hits both Stephen King and Richard Bachman, do we surface only the canonical, or both? Probably only the canonical for the v1 result list; alias awareness through the search itself (typing "Bachman" matches King's row).
- **Pure HTML vs Razor static-SSR for `/bookshop`** — the current plan uses Razor static-SSR. Pure HTML would simplify the SW caching but loses Easy Auth / route handling. Razor static-SSR is the right call unless something blocks during PR3.
- **Where the API endpoint lives** — Minimal API at `Program.cs` or a dedicated `Api/CatalogController.cs`. Probably a controller since it's the project's first real API endpoint and it'll set the pattern; defer to PR1.

## What this is explicitly NOT

- **Not a full WebAssembly Blazor migration.** Reuses Blazor Server for everything except `/bookshop`. The static-SSR + JS approach for one route doesn't propagate.
- **Not a sync-back / offline-write model.** Bookshop mode is read-only-lookup. Adds, edits, status changes all require an online round-trip via the existing edit flows. (v2 could explore "queue offline adds and replay when online," but the use case for that is much narrower than the lookup case.)
- **Not a replacement for the existing PWA install.** The whole BookTracker app stays installable as one PWA; `/bookshop` is just the route within it that's offline-first.
- **Not a generic offline-first refactor of every page.** Only the lookup paths (ISBN check, author search) need offline. Edit pages are deliberately online-only — adding a book is a network-required event by design.

## Cross-references

- The mobile-latency root-cause analysis lives in [the May 8 perf-investigation blog post](../blog/2026-05-08-01-i-blamed-the-cold-start-the-trace-disagreed.md). The "Postscript: the deploy itself was the incident" section + the postscript-meta-lesson section frame why static-pattern fixes alone won't hit the demo-quality bar.
- The existing PWA caching strategy (network-first with cache fallback for static assets, `/_blazor/*` passthrough) is documented in `ARCHITECTURE.md` "Progressive Web App (PWA)" section. PR 2's SW changes layer on top, not over.
- TODO #12 (mobile expander click latency) is a sub-symptom of the same root cause this design addresses; it should close as a side-effect of PR 3 + PR 4 since lookup paths in `/bookshop` won't go through SignalR at all.
- TODO #26 (edit-book page revisit) is unrelated — that's an architectural call for the *edit* flows, which stay online-only here.
