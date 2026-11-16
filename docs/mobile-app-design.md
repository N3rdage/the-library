# Mobile companion app — design

A separate **.NET MAUI** app that owns the offline-mobile-lookup use case, decoupled from the Blazor monolith — a real native companion that doesn't have to fight the Blazor circuit to feel native. It consumes the same catalog data shape as `/bookshop`.

## Why split

`/bookshop` doesn't belong in the Blazor app:

- Page has zero Blazor interactivity by design (no `@onclick` / `@bind` / `@code` beyond a one-shot `OnAfterRenderAsync` JS init kick) — every event handler is plain DOM JS.
- Page deliberately avoids the SignalR circuit but the project's global rendermode forces one anyway.
- A whole parallel data layer (`catalog-cache.js`, IndexedDB schema, SW route-specific caching) exists only to escape Blazor Server's connection requirement.
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
4. **Native primitives matter for this app.** Camera/barcode is one of the killer flows; native camera APIs + native ZXing bindings beat the WebRTC `getUserMedia` path the PWA uses.

Acknowledged costs: Apple Developer account ($99/yr) + a Mac in the build path for iOS; Play Console one-time fee ($25); two more platforms to test on. Distribution can stay TestFlight / Play internal-testing for personal use until / unless the project goes more public.

## Architecture

```
┌─────────────────────────────────────┐    ┌──────────────────────────────┐
│  BookTracker.Mobile (MAUI)          │    │  BookTracker.Web             │
│  Android only (iOS deferred)        │    │  Blazor Server, Azure        │
│  net10.0-android target             │    │  net10.0                     │
│                                     │    │                              │
│  • MSAL (AAD interactive sign-in)   │───▶│  • /api/catalog-snapshot     │
│  • HttpClient + bearer token        │    │  • /api/wishlist-snapshot    │
│  • SQLite local cache (catalog +    │    │                              │
│    wishlist + series)               │    │  • Easy Auth (token-aud      │
│  • ZXing.Net.Maui scanner           │    │     mode, accepts the        │
│  • Tabs: Find / Wishlist / Gaps     │    │     mobile audience)         │
│    (+ status sheet)                 │    │                              │
└─────────────────────────────────────┘    └──────────────────────────────┘
```

API stays put. The mobile app is a pure consumer.

## Authentication

App Service Easy Auth runs in v2 mode and already accepts both browser cookie flows AND bearer-token API calls — the existing `validation.allowedAudiences` array (`['api://${authClientId}', authClientId]` in `infra/modules/app-config.bicep`) is what gates token validation. When MAUI's MSAL acquires a token for scope `api://<authClientId>/access_as_user`, the token's `aud` claim is `api://<authClientId>` — already in the allowed list. **No Bicep change is needed** (no `aadAcceptedTokenAudiences` wiring) — the existing config covers the mobile flow.

What IS needed is one-time AAD-side setup (manual Azure Portal click-ops; runbook in [`infra/README.md`](../infra/README.md) under "Mobile companion — AAD setup"):

1. **Expose the API scope** on the existing `Library-Patrons` Web app registration: Expose an API → confirm Application ID URI = `api://<authClientId>` → Add scope `access_as_user`.
2. **Register a new AAD app** for the mobile client (Mobile and desktop applications, public client / native) in the same tenant. Redirect URI uses Android's custom URI scheme `msauth://com.thelibrary.mobile/<base64-signature-hash>` (signature hash is derived from the signing keystore).
3. **Grant the mobile app reg API permission** to the Web app's `access_as_user` scope (delegated, with admin consent if the tenant requires it).

Then in MAUI:

4. **MSAL.NET** (`Microsoft.Identity.Client` + `Microsoft.Maui.Authentication`) does the interactive sign-in once via Chrome Custom Tabs, refresh-token from then on. Tokens stay in MSAL.NET's own built-in mobile cache in the app's private storage; the app adds no cache serializer and does not use `SecureStorage`. MSAL.NET's documentation calls that mechanism secure but does not say whether it is encrypted at rest, so the cache is treated as protected by the app sandbox rather than by the Keystore. `Platforms/Android/AndroidManifest.xml` sets `android:allowBackup="false"`, so the app's data, the token cache included, is left out of Android's cloud Auto Backup. The manifest declares no data-extraction rules, and on Android 12 and later `allowBackup` does not govern device-to-device transfer.
5. **HttpClient** attaches `Authorization: Bearer <token>` per request; intercept 401 and re-trigger interactive sign-in.

The browser flow on `/bookshop` keeps working through the same cookie-mode Easy Auth until that route is retired.

## API surface

- `GET /api/catalog-snapshot` — books, authors, series; consumed by `/bookshop` and Bookshelf. `BookSnapshot` carries `seriesId` + `seriesOrder` and `CatalogSnapshot` has a top-level `series` array (id, name, type, expectedCount), which enables the "missing books in series" view client-side.
- `GET /api/wishlist-snapshot` — wishlist rows (id, title, author, isbn, priority, seriesId, seriesOrder). Read-only.
- "Favourite authors" — there is no favourites model, and Bookshelf has no favourites view. The nearest thing is Bookcase's top-10 authors card on the `/` dashboard.

Deferred to v2 if the use case actually surfaces:
- `POST /api/books/{id}/status` — mark read from mobile. Mobile queues writes offline + syncs on reconnect. Drew's own framing: "honestly could use the other app for that," so this stays out of v1.

## What stays in BookTracker.Web

Everything except `/bookshop`. Specifically:

- All edit / capture pages: `/books/add`, `/books/bulk-add`, `/books/{id}`
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
- `BarcodeScanner.startJs` (the pure-JS variant used by `/bookshop` — the `start` Blazor variant stays since `/books/add` and `/shopping` use it)

`/shopping` is not folded into `/bookshop`: with `/bookshop` leaving the main app there is nothing to consolidate into, and `/shopping` keeps its shape as the desktop wishlist surface.

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
