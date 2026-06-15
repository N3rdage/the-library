# Bookshelf (Mobile) — UI Design Spec

**Audience:** a design agent (or designer) producing a visual / UX design for the BookTracker **Bookshelf** mobile app.
**Status:** describes the app as it exists today (2026-06) so a redesign starts from ground truth. Where this disagrees with the code, the code wins — verify against the files cited.

> **How to use this doc.** Sections 1–3 are **hard context and constraints** the design must respect (platform, tech, product purpose). Sections 4–7 describe the **current design** — all of it is on the table for a redesign unless flagged as a constraint. Section 8 lists known gaps worth addressing. Section 9 is the **design brief** — read it first if you just want "what should I produce."

---

## 1. Product context

**BookTracker** is a personal book-collection manager with two surfaces that share one brand:

- **Bookcase** — the full web app (Blazor, `books.silly.ninja`), also installable as a PWA. Where capture, editing, and browsing happen.
- **Bookshelf** — *this app*. A .NET MAUI **Android** companion. Its whole reason to exist is the **in-bookshop use case**: you're standing in a second-hand bookshop, possibly with no signal, and you need fast answers to:
  - *Do I already own this?* (scan the barcode)
  - *What does this author have, and do I have it?* (search by author/title)
  - *Which volumes of this series am I missing?* (series gaps)
  - *Is this on my want-list?* (wishlist)

Design implications that flow from that use case:
- **Offline-first.** Everything reads from a local SQLite cache. No spinner-on-every-tap; data is already on the device.
- **One-handed, glanceable, fast.** Phone in one hand, book in the other. Big tap targets, high-contrast results, minimal chrome.
- **Read-only in v1.** Bookshelf does not add/edit/delete. "Add this", "edit", "open detail" actions **deep-link out to the Bookcase web app** (`Launcher.OpenAsync(...)`). The one exception is the wishlist "Bought" flag (a local toggle).
- **Same brand as Bookcase, not a sub-brand.** The cream-and-leather palette, espresso header, and brass accents should feel continuous with the web app. See `docs/STYLE-GUIDE.md` (the canonical, cross-surface style reference — this spec summarises its Mobile-relevant parts).

---

## 2. Tech stack & hard constraints

These are fixed for v1. A design that requires changing them is out of scope unless explicitly proposed and justified.

| Area | Reality |
|---|---|
| Framework | **.NET MAUI**, target `net10.0-android`. **Android only** for v1 (iOS deferred until/unless an Apple dev account exists; keep designs platform-agnostic so iOS can be added later). |
| Navigation | **`NavigationPage` stack** — *not* Shell, no tab bar, no flyout. Forward via `Navigation.PushAsync`; back via the system nav bar. Root (`MainPage`) hides the system bar and shows its own header banner; child pages use the system back button. |
| UI toolkit | Plain MAUI controls (`Button`, `Border`, `Grid`, `CollectionView`/`VerticalStackLayout`, `Label`, `Entry`, `Image`). **No MudBlazor** (that's web). **No custom control library yet** — cards are constructed in code-behind, not as reusable components. |
| Theming | **No theme provider.** MAUI has no MudBlazor-equivalent token system here, so palette values are **inline hex literals in XAML**, manually mirrored from the web theme. A `Colors.xaml` ResourceDictionary exists but its palette entries are mostly **not yet consumed** (migration is partial — TODO #37). |
| Fonts | **OpenSans-Regular** + **OpenSans-Semibold**, registered in `MauiProgram.cs` and applied via the default `Label`/`Button` styles. (The style guide's "Roboto/system" note is stale — code uses OpenSans.) |
| Offline cache | `BookTracker.Mobile.Cache` — **sqlite-net-pcl** SQLite store. Pulls a slim JSON `CatalogSnapshot` from Bookcase, caches Books/Authors/Editions/Series + a separate Wishlist snapshot. Delta-sync via an `?since` watermark. |
| Auth | **MSAL** public-client sign-in against the same Entra app registration as Bookcase's Easy Auth. |
| Scanner | **ZXing.Net.MAUI** `CameraBarcodeReaderView` (EAN-13 / EAN-8). |
| Networking | Typed `ApiClient` over `HttpClient`; a named `"covers"` client fetches cover images. |
| Icons | **Emoji glyphs inline in text** (📷 📚 👤 🔍 ⭐ ↻ 📖) + a star-string rating (`★★★☆☆`). **No Material/SVG icon set** in-app. App launcher icon + splash are SVG (espresso/brass/parchment). |

**Cache capabilities available to the UI** (`ICatalogCache`): `LookupByIsbn`, `LookupByAuthor`, `SearchAuthors`, `SearchBooksByTitle`, `GetSeriesGaps`, `GetBookEnrichedDetail` (works + editions for a found book), `EnsureCoverCached`, wishlist read + `IsWishlistedIsbn` + local "bought" flag, `GetMeta` (counts + last-synced). A redesign can lean on any of these without new backend work.

---

## 3. Brand & visual system (the palette is a constraint; its *application* is open)

The **palette itself is a brand constraint** — keep the leather/brass/parchment identity. *How* it's applied (layout, hierarchy, density, motion) is open to redesign.

### Palette (mirrored from `BookTracker.Web/Theme/BookTrackerTheme.cs`)

| Name | Hex | Role |
|---|---|---|
| Leather | `#6B2737` | Primary actions, primary text emphasis |
| Brass | `#A67B3A` | Accent borders, **in-bookshop "killer" CTAs** (Scan, Wishlist), active accents |
| Parchment | `#FAF6EC` | Page background |
| Aged parchment | `#F2EADB` | Card / panel surface |
| Espresso | `#3E2723` | Header banner / top app bar |
| Ink | `#2C2416` | Primary text on parchment/brass |
| Faded ink | `#6B5D4A` | Secondary text, hints, status labels |
| Green leather | `#3D5A3F` | Tertiary; rare, reserved for state accents |

**Muted status colours** (deliberately desaturated to sit inside the palette): Success `#4F6B3D`, Info `#3A6B7A`, Warning `#B8861B`, Error `#9B3B2E`.

**Scan-target green is an intentional exception:** the barcode scan window uses Material green `#4CAF50` (not the palette) because "green = recognised barcode area" is a cross-app convention worth keeping. Don't brand-ify the scan reticle.

### Contrast (must hold for any new pairing)

Verified AAA/AA pairings: parchment-on-leather ≈ 9.4:1, ink-on-parchment ≈ 13.3:1, ink-on-brass ≈ 4.7:1 (AA for large/UI), faded-ink-on-parchment ≈ 4.9:1. **Never set body text in brass on parchment** (≈ 3.5:1 — fails); brass is for accents, borders, and ink-on-brass button text only.

### Typography rungs (Mobile uses explicit `FontSize` per control)

| px | Use |
|---|---|
| 24 | Page title in header banner |
| 18 | Primary/killer CTA button text |
| 16 | Standard button text; card titles (bold) |
| 15 | Panel content; wishlist row title |
| 14 | Body labels, meta, outlined buttons |
| 13 | Secondary text, hints, subtitles |
| 12 | Microcopy, timestamps, ISBNs (Courier New monospace) |
| 11 | Footnotes (edition format details, build SHA) |

Weights: **Bold** for titles, primary CTA text, section headings, found-book titles. Regular for everything else. No semibold-as-a-third-weight convention yet.

### Spacing, sizing, shape

- **Spacing rungs (dp):** 4, 6, 8, 12, 14, 16, 20, 24, 32. Snap to these.
- **Touch targets:** 44 dp (low-emphasis, e.g. Sign out — Material minimum), 56 dp (standard primary actions), **64 dp (in-bookshop killer actions — Scan)**, deliberately taller so the headline action stands out.
- **Corner radius:** 8 dp on essentially everything (cards, buttons, panels). Distinctive-but-not-fully-round.

### Component patterns (current reference implementations)

- **Primary CTA:** leather bg, parchment text, bold, 56 dp, radius 8. (`MainPage.xaml` Sign in / Load catalog.)
- **Brass-accent killer CTA:** brass bg, ink text, 64 dp — *the* action that defines the screen (Scan, Wishlist).
- **Card / passive panel:** `Border` with 1px brass stroke, aged-parchment bg, radius 8, padding 12–16. (Cache-stats panel, all result cards.)
- **Found-state result card:** same shape, tinted green for success (border `#A3D9A5` light / `#356735` dark; bg `#E8F5E9` light / `#1B3320` dark) — uses `AppThemeBinding` for dark mode.
- **Outlined low-emphasis button:** transparent bg, faded-ink text, brass border, 44 dp (Sign out, dismiss).
- **Header banner (root pages):** espresso bg, parchment title + brass subtitle; root page hides the system nav bar and renders this instead.

---

## 4. Information architecture & navigation

Single-level hub-and-spoke. `MainPage` is the hub; every feature is a `PushAsync` away and returns via the system back button.

```
MainPage (hub, no system nav bar, espresso banner)
├─ ScanPage              "Scan ISBN"      (camera → found/missing card)
├─ AuthorSearchPage      "Find by author" ──► AuthorBooksPage  "Books by {Author}"
├─ TitleSearchPage       "Find by title"  (book cards; no tap-through in v1)
├─ SeriesGapsPage        "Series gaps"    (read-only list)
└─ WishlistPage          "Wishlist"       (rows + "Bought" toggle + refresh)
```

There is **no tab bar or flyout** today. If a redesign proposes one (e.g. a bottom nav for Scan / Search / Gaps / Wishlist), call that out explicitly as an IA change — it's a reasonable thing to evaluate given the four co-equal in-shop tasks, but it's a deviation from the current `NavigationPage` model.

---

## 5. Feature / page inventory (current state)

### 5.1 MainPage — the hub
- **Header:** espresso banner, "Bookshelf" (24 bold, parchment) + "Offline-capable companion" (13, brass).
- **Auth state machine:** signed-out shows **Sign in**; signed-in shows the action stack + **Sign out** (low-emphasis) + build/version footer.
- **Action stack (56 dp leather unless noted):** Load catalog 📚 · **Scan ISBN 📷 (64 dp brass)** · Find by author 👤 · Find by title 🔍 · Series gaps 📚 · **Wishlist ⭐ (brass)**. Scan + Wishlist pop visually as the two in-shop killers.
- **Cache-stats panel:** brass-bordered parchment card — "Cached catalog", book/author counts, "Last synced X ago".
- **Activity indicator** (leather) + **status label** (faded ink, centered, wraps) for load/sync feedback.

### 5.2 ScanPage — the headline feature
- System nav bar ("Scan ISBN" + back).
- **Camera viewfinder** (ZXing) with a **letterbox overlay**: dark `#B3000000` mask + a centered ~3.5:1 (book-barcode aspect) scan window outlined in Material green `#4CAF50`, 3 px.
- **Wishlist flag** (pre-positioned above results, brass-accented): "⭐ On your wishlist" when the scanned ISBN matches a wishlist row — visible regardless of found/missing.
- **Found card** (green-tinted): cover (80×120, 📖 parchment placeholder → async image) · title (18 bold) · authors (formatted, aliases rolled up) · rating+status (`★★★☆☆ · Read`) · ISBN (mono). Conditional inline sections:
  - **Works** — only for multi-work compendiums; one row per work ("Title — contributors").
  - **Editions** — only for multi-edition books; small cover + format string (`Hardcover · 3rd ed.`) + ISBN per row.
  - **Open in app →** deep-links to `/books/{id}` on Bookcase.
- **Missing card** (neutral grey-tinted): "Not in your library." + ISBN + **Add in app →** (deep-links to the web add-book form with the ISBN pre-filled).
- **Manual entry row:** numeric `Entry` + "+X" (ISBN-10 check digit) + "Lookup".
- Full **dark-mode** support via `AppThemeBinding` on this page.

### 5.3 AuthorSearchPage → AuthorBooksPage
- **Search:** auto-focused entry, **250 ms debounce, min 2 chars**, cancellation-token guarded. Hint explains pen-name aliases roll up to the canonical author.
- **Author cards:** brass-bordered parchment; name (16 bold) + "N books" (pluralised). Tap → AuthorBooksPage.
- **AuthorBooksPage:** header count + book cards. Each card: cover (56×84, 📖 placeholder → async) + title (16 bold) + meta (`★★★☆☆ · Read`). Tap → deep-link to the web book detail.

### 5.4 TitleSearchPage
- Same debounced search + **identical book-card shape** as AuthorBooksPage (deliberate visual reuse). No tap-through in v1.

### 5.5 SeriesGapsPage
- Read-only. One card per series with gaps: series name (16 bold) + progress ("N of M owned") + **missing volumes in leather** (e.g. `#2, #6` or `#2–#4` for runs; interquels like `#4.5` render with their display label, never claim a numbered slot).

### 5.6 WishlistPage
- **Refresh** ↻ (pulls `/api/wishlist-snapshot`) + activity indicator + status/empty-state label.
- **Rows:** cover (48×68) + title (15 bold) + author (13) + **badges** (priority pill — red/amber/green for high/med/low — and optional `#N` series-order pill) + **Bought** button (brass, 40 dp). "Bought" sets a local flag that hides the row but survives cache refresh.

---

## 6. Interaction & async patterns (keep these behaviours)

- **Async cover loading:** every cover starts as a 📖 emoji placeholder on a parchment tile, then swaps in the real image fire-and-forget once cached. Design must accommodate a graceful placeholder→image transition and books that never get a cover.
- **Debounced live search:** results render incrementally as the user types (250 ms / 2-char floor), stale results cancelled.
- **Scan debounce:** ~3 s between detections to avoid rapid re-fires of the same barcode.
- **Deep-link-to-web for any mutation:** "Add", "edit", "open detail" leave the app. Design these as clearly outbound ("→ in app") rather than in-place editors.
- **Best-effort enrichment:** wishlist-flag and enriched works/editions are fetched separately and may pop in after the core result; never block the primary result on them.

---

## 7. Current screens at a glance (ASCII)

```
MAINPAGE                         SCANPAGE (found)              WISHLIST
┌────────────────────────┐      ┌────────────────────────┐   ┌────────────────────────┐
│ ▓ Bookshelf            │      │ [ camera viewfinder ]  │   │  ↻ Refresh             │
│ ▓ Offline companion    │      │   ┌──────────────┐     │   │  3 books on wishlist   │
├────────────────────────┤      │   │ green target │     │   │ ┌────────────────────┐ │
│  📚 Load catalog       │      │   └──────────────┘     │   │ │[c] Title       [Bought]│
│ ╔════════════════════╗ │      │ ⭐ On your wishlist     │   │ │    Author          │ │
│ ║ 📷 Scan ISBN       ║ │      │ ┌────────────────────┐ │   │ │    [High] [#5]     │ │
│ ╚════════════════════╝ │      │ │[cover] Title (18b) │ │   │ └────────────────────┘ │
│  👤 Find by author     │      │ │ Authors            │ │   └────────────────────────┘
│  🔍 Find by title      │      │ │ ★★★☆☆ · Read       │   (brass border, parchment card)
│  📚 Series gaps        │      │ │ 9780… (mono)       │ │
│ ╔════════════════════╗ │      │ │ Works / Editions   │ │
│ ║ ⭐ Wishlist        ║ │      │ │ Open in app →      │ │
│ ╚════════════════════╝ │      │ └────────────────────┘ │
│ ┌ Cached catalog ────┐ │      └────────────────────────┘
│ │ 1,240 books · 312… │ │       brass = killer CTA
│ │ synced 2h ago      │ │       leather = primary
│ └────────────────────┘ │
│  Sign out (outlined)   │
└────────────────────────┘
```

---

## 8. Known gaps & design opportunities

Things the current UI doesn't do well — fair game for the redesign to fix:

1. **Dark mode is partial.** ScanPage result cards swap via `AppThemeBinding`, but **MainPage and the search/gaps/wishlist pages are light-only**. In system dark mode the home screen renders its light palette against dark chrome and clashes. A full dark variant of the leather/brass palette is wanted (TODO #37). **A redesign should specify both light and dark.**
2. **Emoji-as-icons.** Functional but inconsistent across devices and not very "designed". A proper icon treatment (consistent line/duotone set in the palette) is open — but if you introduce an icon set, replace emoji everywhere on a surface (don't mix).
3. **No reusable components.** Cards are built imperatively in code-behind; there's no shared "BookCard" / "ResultCard" component. A redesign that defines a small, consistent component kit (book card, result card, badge, killer-CTA) would be valuable and is implementable as MAUI `ContentView`s.
4. **Search has no tap-through on titles** and no empty/zero-result illustrations; states are bare text labels.
5. **Series gaps & wishlist are flat lists** with no grouping, sorting, or imagery beyond covers — room for a more scannable in-shop layout.
6. **The hub is a vertical button stack.** Works, but evaluate whether a bottom-nav or a more spatial home better serves four co-equal in-shop tasks (see §4 — this is an IA change to call out, not assume).
7. **Palette is hand-mirrored hex.** If the design introduces new tokens, prefer expressing them as a `Colors.xaml` ResourceDictionary the app can adopt (consolidation is already a tracked goal).

---

## 9. Design brief — what to produce

**Goal:** a cohesive visual + interaction design for the Bookshelf Android app that (a) keeps the leather/brass/parchment brand, (b) optimises for the fast, one-handed, offline in-bookshop moment, and (c) closes the gaps in §8.

**Deliverables requested:**
1. **Full screen designs** for all six surfaces (MainPage/hub, Scan found+missing, Author search→books, Title search, Series gaps, Wishlist) in **both light and dark**.
2. A **component kit**: book card, result card (found/missing), badge (priority / series-order / wishlist), primary CTA, killer CTA, header, empty/zero-result states, loading/placeholder states.
3. **Navigation/IA recommendation:** keep the hub-and-spoke `NavigationPage` model, or move to bottom-nav? Justify against the four in-shop tasks.
4. **A revised token set** (colours incl. dark variants, type scale, spacing, radius) expressed so it can drop into a MAUI `Colors.xaml` / `Styles.xaml`.

**Constraints to honour (from §2–§3):**
- MAUI Android, plain controls, `NavigationPage` (or a justified IA change). No web/MudBlazor patterns.
- Keep the palette identity and the contrast floors in §3. Keep the green scan reticle.
- Respect touch-target rungs (44/56/64) and the 8 dp radius language unless proposing a deliberate system-wide change.
- Read-only app: mutations deep-link to web — design outbound affordances, not in-app editors.
- Offline-first: design for instant local data + async cover/enrichment pop-in + graceful no-cover/no-result states.

**Open framing question for Drew to set** *(pick one before the design agent starts):*
- **(A) Refresh** — same IA and feature set, tighten the visual system, add dark mode + an icon set + a component kit. *(Lower risk; recommended default.)*
- **(B) Rework** — reconsider IA (bottom-nav, spatial home), richer list layouts, and interaction flows, within the same tech/brand constraints.
- **(C) Blue-sky** — propose the ideal in-shop experience first, then note which parts exceed the current tech constraints.

**Reference files** (ground truth): `docs/STYLE-GUIDE.md` (full cross-surface style system), `BookTracker.Mobile/MainPage.xaml`, `BookTracker.Mobile/Pages/*.xaml(.cs)`, `BookTracker.Mobile/Resources/Styles/{Colors,Styles}.xaml`, `BookTracker.Web/Theme/BookTrackerTheme.cs` (canonical palette).
