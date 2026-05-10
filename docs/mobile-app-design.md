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
│  iOS + Android                      │    │  Blazor Server, Azure        │
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

App Service Easy Auth currently runs in cookie mode (browser flow, redirect to `/.auth/login/aad`). For the mobile client, switch the relevant API endpoints to **token-audience acceptance** so a bearer token from MSAL works:

1. **Register a new AAD app** for the mobile client (native / public client type) in the same tenant. Custom URI scheme redirect (`msauth.com.thelibrary.mobile://auth`) for iOS / Android.
2. **Expose an API audience** on the existing BookTracker.Web AAD app registration (e.g. `api://<web-app-client-id>/access_as_user`).
3. **Wire `aadAcceptedTokenAudiences`** in App Service Auth config so the API endpoints accept tokens issued for that audience.
4. **MAUI uses MSAL.NET** (`Microsoft.Identity.Client` + `Microsoft.Maui.Authentication`) to do the interactive sign-in once, refresh-token from then on. Token cached in `SecureStorage` (Keychain / Keystore).
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

### PR 2 — Easy Auth token-audience mode + AAD app reg for mobile
- Bicep change to add `aadAcceptedTokenAudiences` for the mobile audience.
- New AAD native-client app registration (manual one-time setup, document in `infra/README.md`).
- Validate end-to-end: get a token via `az account get-access-token --resource <audience>` on the dev box, hit the API with it, see the response.
- **Web-only PR.** No MAUI yet.
- Size: S.

### PR 3 — `BookTracker.Mobile` MAUI project skeleton + auth flow
- New project under repo root. iOS + Android targets.
- MSAL.NET interactive sign-in → token cached in `SecureStorage`.
- Single "Hello, signed-in user" page that calls `GET /api/catalog-snapshot` and dumps the response count.
- Build pipeline: side-load APK on Android via `dotnet build -t:Run -f net10.0-android`. iOS deferred until Apple dev account is in place.
- Size: M (lots of first-time setup).

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
- Build pipeline: GH Actions workflow for Android APK build artifact (so Drew can pull a built APK without needing the dev box).
- Size: M.

**Total: 8 PRs, sized M heavy.** Real calendar effort probably 10-15 sessions for a v1 the user can install and use daily.

## Open questions to resolve before PR 1

- **Repo shape:** add `BookTracker.Mobile` as a project alongside the existing two, or split into a sibling repo (`the-library-mobile`)? Recommendation: same repo. Coupled lifecycle (mobile follows API contract changes), shared GH Actions workflows for Android builds, single source of truth for issues and PRs. Splitting later is easy if the coupling becomes painful; merging later is harder.
- **Apple developer account:** required only for iOS. Android-only v1 is a perfectly valid first cut and probably the right way to ship. iOS as a follow-on once the shape stabilises.
- **`BookTracker.Shared` scope:** ship just the API DTOs first. Resist the urge to share business logic between Web and Mobile — drift is fine, divergence in display logic is *expected* (a desktop card and a mobile screen don't share rendering code).
- **MAUI version target:** .NET 10 if MAUI's .NET 10 release is out by the time PR 3 starts; .NET 9 LTS otherwise. The Web app is on net10.0; same target keeps the toolchain uniform but isn't required.

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
- Side-loadable APK on Android via GH Actions artifact.
- `/bookshop` route in BookTracker.Web stays available throughout the arc; gets retired only after MAUI v1 has been Drew's daily mobile driver for a few weeks without regret.
