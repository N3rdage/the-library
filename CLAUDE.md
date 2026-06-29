# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Environment

**Windows only.** All commands in this file and any you suggest must target a Windows shell (PowerShell 7 / Windows PowerShell / `cmd.exe`). Do not use Unix-isms like `/dev/null`, forward-slash-only paths, `export VAR=...`, `&&` chaining habits from bash scripts, or POSIX tools (`grep`, `sed`, `cat`) in instructions. Use `;` to chain in PowerShell, `$env:VAR = "..."` to set env vars, backslashes or quoted forward-slash paths, and `Select-String` / `Get-Content` when a shell tool is needed. The repo path contains a space (`C:\Users\Drew.Work\code\The Library`) — always quote it.

## Project

BookTracker is the codebase / namespace umbrella for two apps:

- **Bookcase** — ASP.NET Core Blazor Web App (net10.0, Interactive Server), backed by EF Core + Azure SQL. PWA-installable. Deployed at `books.silly.ninja`.
- **Bookshelf** — .NET MAUI Android companion. Offline-capable in-bookshop tool. Read-only against a slim JSON catalog snapshot served by Bookcase.

AI features (Anthropic / Azure OpenAI / Microsoft Foundry) live in Bookcase.

## Architecture

Six-project solution (`BookTracker.slnx`, the new XML solution format):

- **`BookTracker.Web\`** — Blazor Web App host (Bookcase). Owns `Program.cs`, `ProgramSetup.cs`, `appsettings*.json`, Razor components under `Components\` (`App.razor`, `Routes.razor`, `Layout\MainLayout.razor`, pages under `Components\Pages\`), services under `Services\`, and Minimal API endpoints under `Api\`. The whole app is rendered with `@rendermode="InteractiveServer"` set globally via `<Routes>` in `App.razor` — no per-page render-mode attributes. Uses net9+ `MapStaticAssets()` pipeline.
- **`BookTracker.Data\`** — Class library holding `BookTrackerDbContext`, entities in `Models\`, EF migrations in `Migrations\`, and the `BookUpdatedAtInterceptor` under `Interceptors\`. EF tooling (`Microsoft.EntityFrameworkCore.Tools`) lives here, so migrations run against this project with `BookTracker.Web` as the startup project (so config + connection string resolve).
- **`BookTracker.Shared\`** — Wire-format DTO records (`CatalogSnapshot`, `BookSnapshot`, `EditionSnapshot`, `WorkSnapshot`, `AuthorSnapshot`, `SeriesSnapshot`) under `Catalog\`. Zero EF dependency so Mobile can reference them without dragging in `BookTracker.Data`. Web projects from EF into these; Mobile + the PWA `/bookshop` deserialise from the same shapes.
- **`BookTracker.Mobile\`** — .NET MAUI Android app (Bookshelf), `net10.0-android`. Pages under `Pages\`. Auth via MSAL public-client against the same Entra app reg as Bookcase's Easy Auth.
- **`BookTracker.Mobile.Cache\`** — sqlite-net-pcl-backed `CatalogCache` library, pure `net10.0`. Exposes `ICatalogCache` with `PopulateAsync` / `ApplyDeltaAsync` / `LookupByIsbn` / `LookupByAuthor` / `SearchAuthors` / `SearchBooksByTitle` / `GetSeriesGaps` / `GetBookEnrichedDetail` / `EnsureCoverCached` / `GetMeta`. Pure-net10 so it tests cleanly without the MAUI runtime.
- **`BookTracker.Mobile.Cache.Tests\`** + **`BookTracker.Tests\`** — xUnit test projects (see Tests section below).

**DbContext lifetime:** Blazor Server circuits are long-lived while `DbContext` is scoped and not thread-safe. `ProgramSetup.cs` registers `AddDbContextFactory<BookTrackerDbContext>`; components inject `IDbContextFactory<T>` and create/dispose a context per operation (`await using var db = await DbFactory.CreateDbContextAsync();`). Do **not** switch back to `AddDbContext` + direct injection.

Entity model: `Book` (Title, `BookStatus`, Rating, Notes, DateAdded, DefaultCoverArtUrl, **UpdatedAt** for delta-sync, **DeletedAt** for soft-delete, many-to-many `Works`, one-to-many `Editions`, many-to-many `Tags`), `Work` (Title, Subtitle, AuthorId/Author, FirstPublishedDate, many-to-many `Genres`, optional `Series` + SeriesOrder, many-to-many `Books`), `Author` (Name unique, nullable self-FK `CanonicalAuthorId` for pen-name aliases, one-to-many `Works`), `Edition` (filtered-unique nullable Isbn, `BookFormat`, DatePrinted, CoverUrl, Publisher, one-to-many `Copies`), `Copy` (Condition, DateAcquired, Notes), `Genre` (hierarchical with parent/child, many-to-many to Work), `Series` (Name, Author string display-only, `SeriesType` Series/Collection, ExpectedCount, one-to-many `Works`), `Tag` (many-to-many with Book), `Publisher`, and `WishlistItem` (Title, Author, Priority, optional Isbn/Series link). The Work refactor sits the abstract creative unit (story / novel / play) above the physical Book grouping — single-Work books are the common case (Add page auto-creates one alongside the Book); compendiums attach extra Works via the Book Detail "Add Work" dialog (the `/books/{id}/edit` page was decommissioned). Authorship, subtitle, genres, and series live on the Work, not the Book. Pen names: each Work points at a specific Author row; aliases (e.g. Bachman) carry `CanonicalAuthorId` referencing the canonical (King) so aggregations roll up; `/authors` page manages aliases. Find-or-create on save is auto via `AuthorResolver`.

`Book.UpdatedAt` is bumped automatically on every aggregate change by `BookUpdatedAtInterceptor` (a `SaveChangesInterceptor`). A value converter pins `Kind=Utc` on read so cross-timezone clients round-trip the watermark correctly. `Book.DeletedAt` drives soft-delete via a global EF query filter — tombstoned rows are hidden from every normal query, but visible to `IgnoreQueryFilters()` so the catalog snapshot can emit them in `deletedIds[]`. See `ARCHITECTURE.md` for the full surface.

Config convention: connection string name is **`DefaultConnection`**. Dev value lives in `appsettings.Development.json` and points at the Docker SQL container. Prod and staging slots in Azure point at **separate databases** (`booktracker` vs `booktracker-staging` on the same SQL server) via slot-sticky `DefaultConnection` — the slot swap is purely code-shaped and never moves the DB underneath the bits. `appsettings*.json` is **gitignored**; committed templates live alongside them as `appsettings.Example.json` and `appsettings.Development.Example.json` — on a fresh clone, copy each `.Example.json` to the real filename and fill in secrets.

## Branching

**Never commit or push to `main`.** All changes go through a feature branch + PR. Branch-name prefixes: `feat/`, `fix/`, `chore/`, `docs/`, `refactor/`. Large features may use sub-branches (`feat/parent/sub-branch`) that PR into the parent feature branch before the parent PRs into `main`. Squash-merge into `main`. See `CONTRIBUTING.md` for full details.

Before starting new work:

```powershell
git checkout main
git pull
git checkout -b feat/<short-name>
```

## Commands (PowerShell)

Run from the repo root. Quote the path because it contains a space.

```powershell
dotnet build .\BookTracker.slnx
dotnet run   --project .\BookTracker.Web
dotnet watch --project .\BookTracker.Web   # hot reload
```

EF Core migrations always need both `--project` (Data) and `--startup-project` (Web):

```powershell
dotnet ef migrations add <Name> --project .\BookTracker.Data --startup-project .\BookTracker.Web
dotnet ef database update       --project .\BookTracker.Data --startup-project .\BookTracker.Web
dotnet ef migrations remove     --project .\BookTracker.Data --startup-project .\BookTracker.Web
```

## Local dev environment

End-to-end setup (Docker Desktop containers for SQL Server + Azurite, mkcert for the local TLS cert that keeps Azurite on HTTPS, EF migration commands) lives in [`docs/LOCAL-DEV.md`](docs/LOCAL-DEV.md). Run through that once on a fresh machine; the daily workflow is `docker compose up -d` then `dotnet watch --project .\BookTracker.Web`.

## Mobile considerations

Two distinct mobile surfaces:

- **Bookcase as a PWA** — installable on mobile + desktop, runs in standalone display mode with the same Blazor Server backend. Mobile-responsive Razor pages; primary in-bookshop surface is `/bookshop` (IndexedDB cache + scan + author lookup tabs).
- **Bookshelf MAUI Android app** — native sibling for the same in-bookshop use case, but offline-capable. Pulls a slim JSON catalog snapshot from Bookcase, caches it in local SQLite. **Shell + 3 bottom tabs** (redesigned 2026-06, see `docs/BOOKSHELF-UI-REDESIGN.md`): **Find** (owned-library search + inline scanner + ISBN → result), **Wishlist** (sortable, grouped), **Gaps** (per-series progress + missing pills); sign-in + sync live in a status sheet off the Find tab's sync chip. v1 is offline search/scan of what you *own*; the "do better when online" layer is a separate arc (TODO #54).

When planning new features, always clarify which surface(s) it touches: **Bookcase web only**, **Bookcase web + PWA mobile**, **Bookshelf only**, or **both apps**. The decision drives where the work lands (Razor + JS, MAUI XAML + cache, or DTO + both consumers). Key mobile workflows: in-bookshop ISBN scan, author/title lookup, series gaps (do I need #6 of Foundation while I'm here?).

## AI integration

Three AI providers supported in code, selectable at runtime via toggle on the AI Assistant and Bulk Add pages:

- **Anthropic** (direct public API) — Claude Sonnet for fast ops, Opus for deep analysis. Provisioned in prod when `-AnthropicApiKey` is supplied to `deploy.ps1`.
- **Microsoft Foundry** (Claude on Azure) — supported in code but **not currently provisioned**: the project's Azure subscription is `Sponsored`, which Microsoft excludes from Claude eligibility on Foundry. See `infra/README.md` and `TODO.md`.
- **Azure OpenAI** — GPT-4o. Provisioned automatically in `eastus2` (with a Private Endpoint and KV-stored key) by `infra/modules/ai-services.bicep`.

Config under `AI:` section in appsettings. Only providers with valid config are available; the picker auto-detects. In prod, secret values resolve via Key Vault references — see `infra/README.md` for the wiring.

## Cover storage

Book cover images are mirrored from upstream providers (Open Library, Google Books, Trove) into Azure Blob Storage so renders never depend on upstream latency. `IBookCoverStorage` (`BookTracker.Web/Services/Covers/`) downloads the upstream URL, normalises via SkiaSharp (JPEG, max 1200px on the long edge — falls back to raw bytes with a logged warning if conversion fails per Drew's call; same imaging library as the Bookshelf cover cache, replacing SixLabors.ImageSharp after it moved to a build-time licence key in v4), uploads to the `book-covers` container, and the URL stored on `Edition.CoverUrl` / `Book.DefaultCoverArtUrl` swaps to the blob URL.

`CoverMirrorBackgroundService` polls every 30s for un-mirrored URLs and processes them in batches of 50. Same service handles both initial backfill (legacy upstream URLs in the DB before this shipped) and ongoing mirroring of newly-added covers — there's no save-site integration in PR1 (PR2 will move new-cover mirroring inline at the save site so the polling becomes backfill-only).

Local: Azurite emulator on `localhost:10000` (see Docker Compose section above). Connection string + endpoint URL are in `appsettings.Development.Example.json` using Azurite's well-known dev account name. Prod: real Storage Account provisioned by `infra/modules/cover-storage.bicep`, connection string lives in Key Vault as `CoverStorageConnectionString` and resolves into the `CoverStorage:ConnectionString` app setting via a KV reference.

## Tests

Two xUnit projects:

- **`BookTracker.Tests\`** — Bookcase tests. xUnit + NSubstitute against a **real SQL Server via MSSQL Testcontainer** (one container per process, Respawn-based wipe + reseed per `TestDbContextFactory`). EF InMemory was the original choice; the pivot off it caught real bugs InMemory's lax SQL translation had let through. Playwright E2E lives in `E2E/` (gated by `Category=E2E`) — requires `ms-playwright` browsers locally.
- **`BookTracker.Mobile.Cache.Tests\`** — Bookshelf cache tests. Pure xUnit + real SQLite files (GUID-named temp files, one per test). No MAUI runtime needed.

CI runs `dotnet test` on all PRs to main. The full suite must stay green before merge.
