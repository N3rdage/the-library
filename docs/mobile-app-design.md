# Mobile companion app — design

A separate **.NET MAUI** app that owns the offline-mobile-lookup use case, decoupled from the Blazor monolith. The bookshop arc proved the data shape; this proves the *delivery* shape — a real native companion that doesn't have to fight the Blazor circuit to feel native.

## Why split

The bookshop arc shipped working, but kept telegraphing that `/bookshop` doesn't belong in the Blazor app:

- Page has zero Blazor interactivity by design (no `@onclick` / `@bind` / `@code` beyond a one-shot `OnAfterRenderAsync` JS init kick) — every event handler is plain DOM JS.
- Page deliberately avoids the SignalR circuit but the project's global rendermode forces one anyway.
- A whole parallel data layer (`catalog-cache.js`, IndexedDB schema, SW route-specific caching) was built to escape Blazor Server's connection requirement.
- The "Static SSR vs InteractiveServer" rendermode question is unresolvable inside the current architecture without restructuring `<Routes>`.

Drew's actual usage cleanly partitions into two apps:

| **Main app (web, online, full CRUD)** | **Mobile companion (offline-capable lookup)** |
|---|---|
| Capture (single + bulk add) | "Do I have this book?" (scan / manual ISBN) |
| Edit / categorise / photo-update | Search authors → see their books |
| Browse, mark read, manage wishlist | Check shopping list (read-only) |
| Tidy genres / series / duplicates | "Missing" books in series, favourite authors |
| AI assistant | Sub-100ms, zero-network-required |

The companion is a strict subset of the main app's read surface, with offline as a hard requirement. The main app stays uncluttered (one less nav item), the companion gets architected for its actual constraints, and the project gets a real native-mobile demo surface.

## Stack: .NET MAUI

Picked over Blazor WebAssembly and PWA-wrap-via-Capacitor for these reasons:

1. **Learning win.** Drew already does WASM and PWA. MAUI is the next step.
2. **Honest "native companion" framing** — compiled iOS/Android binaries, not a WebView with extra steps. Reads better in a portfolio than "PWA wrapped in Capacitor."
3. **C# end-to-end.** Same language as the main app; potential to share DTO contracts via a small `BookTracker.Shared` project (don't share `BookTracker.Data` — EF Core dependency would bleed into mobile).
4. **Native primitives matter for this app.** Camera/barcode is one of the killer flows; native camera APIs + native ZXing bindings beat the WebRTC `getUserMedia` path the PWA uses today.

Acknowledged costs: Apple Developer account ($99/yr) + a Mac in the build path for iOS; Play Console one-time fee ($25); two more platforms to test on. Distribution can stay TestFlight / Play internal-testing for personal use until / unless the project goes more public.

## Architecture

```
┌─────────────────────────────────────┐    ┌──────────────────────────────┐
│  BookTracker.Mobile (MAUI)          │    │  BookTracker.Web (existing)  │
│  Android v1 (iOS deferred)          │    │  Blazor Server, Azure        │
│  net10.0-android target             │    │  net10.0                     │
│                                     │    │                              │
│  • MSAL (AAD interactive sign-in)   │───▶│  • /api/catalog-snapshot     │
│  • HttpClient + bearer token        │    │  • /api/wishlist-snapshot    │
│  • SQLite local cache (catalog +    │    │     (new, this arc)          │
│    wishlist + series)               │    │  • Easy Auth (token-aud      │
│  • ZXing.Net.Maui scanner           │    │     mode, accepts the        │
│  • Pages: Scan / Authors / Series / │    │     mobile audience)         │
│    Wishlist / Settings              │    │                              │
└─────────────────────────────────────┘    └──────────────────────────────┘
```

API stays put. The mobile app is a pure consumer.

## Authentication

App Service Easy Auth runs in v2 mode and already accepts both browser cookie flows AND bearer-token API calls — the existing `validation.allowedAudiences` array (`['api://${authClientId}', authClientId]` in `infra/modules/app-config.bicep`) is what gates token validation. When MAUI's MSAL acquires a token for scope `api://<authClientId>/access_as_user`, the token's `aud` claim is `api://<authClientId>` — already in the allowed list. **No Bicep change is needed**, despite the original design's claim that `aadAcceptedTokenAudiences` had to be wired up — the existing config covers the mobile flow.

What IS needed is one-time AAD-side setup (manual Azure Portal click-ops; runbook in [`infra/README.md`](../infra/README.md) under "Mobile companion — AAD setup"):

1. **Expose the API scope** on the existing `Library-Patrons` Web app registration: Expose an API → confirm Application ID URI = `api://<authClientId>` → Add scope `access_as_user`.
2. **Register a new AAD app** for the mobile client (Mobile and desktop applications, public client / native) in the same tenant. Redirect URI uses Android's custom URI scheme `msauth://com.thelibrary.mobile/<base64-signature-hash>` (signature hash is derived during PR 3 from the dev keystore).
3. **Grant the mobile app reg API permission** to the Web app's `access_as_user` scope (delegated, with admin consent if the tenant requires it).

Then in MAUI (PR 3+):

4. **MSAL.NET** (`Microsoft.Identity.Client` + `Microsoft.Maui.Authentication`) does the interactive sign-in once via Chrome Custom Tabs, refresh-token from then on. Token cached in `SecureStorage` (Keystore on Android).
5. **HttpClient** attaches `Authorization: Bearer <token>` per request; intercept 401 and re-trigger interactive sign-in.

The browser flow on `/bookshop` keeps working through the same cookie-mode Easy Auth until that route is retired.

## API surface

Existing:
- `GET /api/catalog-snapshot` — books, authors. Already shipped, already in use by `/bookshop`.

New (this arc — to be added on BookTracker.Web before the MAUI consumer needs them):
- `GET /api/wishlist-snapshot` — wishlist rows (id, title, author, isbn, priority, seriesId, seriesOrder). Read-only. Sized S.
- Extend `BookSnapshot` with `seriesId`, `seriesOrder`, plus a top-level `series` array on `CatalogSnapshot` (id, name, type, expectedCount). Enables the "missing books in series" view client-side. Sized S.
- "Favourite authors" — defer the model decision. v1 mobile derives "favourites" as "top N authors by book count" (mirrors `/Home`'s top-author tally). If real usage shows that's wrong, add an explicit `Author.IsFavourite` flag in a separate PR.

Deferred to v2 if the use case actually surfaces:
- `POST /api/books/{id}/status` — mark read from mobile. Mobile queues writes offline + syncs on reconnect. Drew's own framing: "honestly could use the other app for that," so this stays out of v1.

## What stays in BookTracker.Web

Everything except `/bookshop`. Specifically:

- All edit / capture pages: `/books/add`, `/books/bulk-add`, `/books/{id}`, `/books/{id}/edit`
- All browse pages: `/books`, `/authors`, `/publishers`, `/series`
- `/shopping` stays (desktop wishlist write surface — no consolidation needed once `/bookshop` leaves)
- `/duplicates`, `/assistant`
- All `/api/*` endpoints — these grow, they don't move

## What moves out (eventually)

When the MAUI app is stable and Drew has stopped using `/bookshop` in the field:

- `Components/Pages/Bookshop/Index.razor` (the page)
- `wwwroot/js/bookshop-page.js`, `wwwroot/js/catalog-cache.js`
- `service-worker.js` route-specific cache-first handling for `/bookshop*` and `/api/catalog-snapshot`
- Bookshop nav link in `MainLayout.razor`
- `BarcodeScanner.startJs` (the pure-JS variant added for `/bookshop` — the `start` Blazor variant stays since `/books/add` and `/shopping` use it)

Cancellation: **TODO #28 (Shopping consolidation arc) — closed without code.** Premise was "fold /shopping into /bookshop tabs." With `/bookshop` leaving the main app, there's nothing to consolidate into. `/shopping` keeps its current shape as the desktop wishlist surface.

## PR / arc breakdown

Sized for the work, not the calendar. Ship-as-you-go.

### PR 1 — `BookTracker.Shared` DTO project + API surface extension
- New `BookTracker.Shared` class library; relocate `CatalogSnapshot` / `BookSnapshot` / `AuthorSnapshot` records (and the new `SeriesSnapshot` / `WishlistSnapshot` types) into it.
- Extend `CatalogSnapshotService` with series data + new `WishlistSnapshotService` with `/api/wishlist-snapshot`.
- Tests on the shape + auth gating.
- **Web-only PR.** No MAUI yet. The DTOs become the contract the mobile app codes against.
- Size: M.

### PR 2 — AAD setup runbook for the mobile client
- Documentation-only PR. The Bicep work originally listed turned out to be unnecessary: the existing `validation.allowedAudiences` in `app-config.bicep` already accepts the audience MSAL produces for the API scope.
- New "Mobile companion — AAD setup" section in `infra/README.md`: expose `access_as_user` scope on the Web app reg; register a new mobile-native-client app reg; wire delegated API permission; record the resulting clientIds.
- Validate end-to-end: get a token via `az account get-access-token --resource api://<authClientId>` on the dev box, hit `/api/catalog-snapshot` with it, see the response.
- **Web-only PR.** No MAUI, no infra change.
- Size: XS (was S — scope shrank when the Bicep claim was disproven).

### PR 3 — `BookTracker.Mobile` MAUI project skeleton + auth flow
- New project at repo root, **`net10.0-android` target only** (iOS deferred until / unless an Apple Developer account materialises).
- MSAL.NET interactive sign-in via `Microsoft.Maui.Authentication.WebAuthenticator` → token cached in `SecureStorage`. AndroidManifest entry for the redirect URI scheme; Chrome Custom Tabs handles the browser hop.
- Single "Hello, signed-in user" page that calls `GET /api/catalog-snapshot` and dumps the response count.
- Dev workflow: Drew connects his phone via USB with developer mode enabled, runs `dotnet build -t:Run -f net10.0-android` from the repo, MAUI deploys + launches via ADB. No store, no APK file shuffling. Same loop as `dotnet watch` for the Web app, just on a phone.
- Size: M (lots of first-time setup — AAD app reg, redirect URI plumbing, MSAL config).

### PR 4 — Local SQLite cache (mirrors PWA's IndexedDB)
- `sqlite-net-pcl` for storage. Schema: books / authors / series / wishlist / meta. Mirrors the IndexedDB schema decisions from `catalog-cache.js`.
- `ICatalogCache` service: `Init`, `Refresh`, `LookupByIsbn`, `LookupByAuthor`, `SearchAuthors`, `GetMeta` — same surface, C# instead of JS.
- Tests against in-memory SQLite.
- Size: M.

### PR 5 — Scan mode page
- ZXing.Net.Maui camera + EAN-13/EAN-8 decode.
- Result card: in-cache / not-in-cache shapes.
- Wires to `ICatalogCache` from PR 4.
- Size: M.

### PR 6 — Author lookup mode page
- Search input → cache lookup → results list → tap-through to author's books.
- Same UX as the bookshop Author tab; native widgets.
- Size: S.

### PR 7 — Wishlist + Series-gaps pages
- Read-only views over the new snapshot shapes (PR 1).
- Size: S.

### PR 8 — Polish + packaging
- Stale-cache warnings, offline indicator, refresh UX, settings screen (sign out, force refresh).
- App icon, splash screen.
- **Optional**: GH Actions workflow that builds an unsigned debug APK on push and uploads as a workflow artifact. Useful only when Drew can't or doesn't want to build locally — the `dotnet build -t:Run` dev loop from PR 3 covers normal use. Defer until the friction surfaces.
- Size: M (S without the GH Actions workflow).

**Total: 8 PRs, sized M heavy.** Real calendar effort probably 10-15 sessions for a v1 the user can install and use daily.

## Decisions

- **Repo shape: same repo.** `BookTracker.Mobile` lives alongside `BookTracker.Web`, `BookTracker.Data`, `BookTracker.Tests`. Coupled lifecycle with API changes, single source of truth for issues, shared CI surface. Splitting into a sibling repo later is easy if coupling becomes painful; merging later is harder.
- **Platform: Android only for v1.** No Apple Developer account, no Mac in the build path, no App Store / TestFlight overhead. iOS becomes a follow-on if and when the use case warrants it; the design above is platform-agnostic enough that adding iOS later doesn't require restructuring.
- **`BookTracker.Shared` scope: API DTOs only.** No shared business logic between Web and Mobile. The DTOs are the contract; everything else is allowed to diverge. Resist the urge to share rendering or service code — a desktop Razor card and a mobile XAML screen don't share view logic, and pretending they do creates friction without saving anything.
- **Target framework: .NET 10.** Same as `BookTracker.Web`. Toolchain uniform, no version-gap papercuts. `net10.0-android` for the MAUI project.

## What this is explicitly NOT

- **Not a rewrite of the Web app in MAUI.** The Web app stays Blazor Server. Mobile is a strict-subset companion.
- **Not a sync-back / offline-write model in v1.** Reads only. Writes (mark read, status changes) happen in the Web app. v2 can revisit if the use case sharpens.
- **Not a shared-UI-component library between Web and Mobile.** MAUI's XAML and Razor's HTML aren't reusable; sharing pretends a uniformity that isn't there.
- **Not a replacement for the existing PWA installation.** The Web app stays installable as a PWA for desktop users. The mobile companion is a parallel install path for phone-first users.

## Success criteria for v1

- Sign in once via AAD → token persists → app opens to the scan page on subsequent launches without re-auth.
- Scan an ISBN with the device camera → answer in <200ms, regardless of network state.
- Author search returns matches as fast as the PWA does today.
- Wishlist + missing-books-in-series views render from cache, refresh on demand.
- Installable on Drew's Android phone via `dotnet build -t:Run -f net10.0-android` over USB. (GH Actions APK artifact is optional; only ships if the local-build loop turns out to be friction.)
- `/bookshop` route in BookTracker.Web stays available throughout the arc; gets retired only after MAUI v1 has been Drew's daily mobile driver for a few weeks without regret.
