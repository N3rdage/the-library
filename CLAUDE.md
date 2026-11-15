# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Environment

**Windows only** for sessions with Drew. All commands in this file and any you suggest must target a Windows shell (PowerShell 7 / Windows PowerShell / `cmd.exe`). Do not use Unix-isms like `/dev/null`, forward-slash-only paths, `export VAR=...`, `&&` chaining habits from bash scripts, or POSIX tools (`grep`, `sed`, `cat`) in instructions. Use `;` to chain in PowerShell, `$env:VAR = "..."` to set env vars, backslashes or quoted forward-slash paths, and `Select-String` / `Get-Content` when a shell tool is needed. The repo path contains a space (`C:\Users\Drew.Work\code\The Library`) — always quote it.

**The one exception: headless orchestrator runs.** Since November 2026 an autonomous controlling agent also works in this repo (one PR per backlog issue, independently reviewed and merged by a second GitHub App; its rules live in the private `N3rdage/agentic-gaming` repo, ADR-003). It runs in a Linux container with bash, so the `dotnet`, `git` and `gh` commands here are the same but the shell idioms above do not apply to it, and it never edits this file to say so. Its branches are `agent/<issue>-<slug>`; it cannot run the Integration test category (Testcontainers needs Docker, which its container lacks), so for its PRs the CI job is the integration gate; it never runs the deploy, swap or rotation workflows, and a major Bookshelf change waits for Drew's yes after review (see Mobile considerations). Drew's veto on any of its PRs is the `needs-drew` label, a changes-requested review, or the word `hold` in a comment.

## Project

BookTracker is the codebase / namespace umbrella for two apps:

- **Bookcase** — ASP.NET Core Blazor Web App (net10.0, Interactive Server), backed by EF Core + Azure SQL. PWA-installable. Deployed at `books.silly.ninja`.
- **Bookshelf** — .NET MAUI Android companion. Offline-capable in-bookshop tool. Read-only against a slim JSON catalog snapshot served by Bookcase.

AI features (Anthropic / Azure OpenAI / Microsoft Foundry) live in Bookcase.

## Architecture

Nine-project solution (`BookTracker.slnx`, the new XML solution format):

- **`BookTracker.Web\`** — Blazor Web App host (Bookcase). Owns `Program.cs`, `ProgramSetup.cs`, `appsettings*.json`, Razor components under `Components\` (`App.razor`, `Routes.razor`, `Layout\MainLayout.razor`, pages under `Components\Pages\`), services under `Services\`, and Minimal API endpoints under `Api\`. The whole app is rendered with `@rendermode="InteractiveServer"` set globally via `<Routes>` in `App.razor` — no per-page render-mode attributes. Uses net9+ `MapStaticAssets()` pipeline.
- **`BookTracker.Data\`** — Class library holding `BookTrackerDbContext`, entities in `Models\` (rich aggregates — invariants live on the entity), EF migrations in `Migrations\`, and the `BookUpdatedAtInterceptor` under `Interceptors\`. EF tooling (`Microsoft.EntityFrameworkCore.Tools`) lives here, so migrations run against this project with `BookTracker.Web` as the startup project (so config + connection string resolve).
- **`BookTracker.Application\`** — The DDD/CQRS spine between UI and data (TODO #55, shipped 2026-06-27). Owns **command** handlers (`ICommandHandler<TCommand>` / `<TCommand,TResult>`; commands marked `ICommand` / `ICommand<TResult>`) **and query** handlers (`IQueryHandler<TQuery,TResult>`; queries marked `IQuery<T>`), plus the read-model projections + formatters. Dispatch goes through a thin hand-rolled **`IDispatcher`** (`Send(ICommand)` / `Query(IQuery<T>)`) — **no MediatR**; handlers are discovered by a convention scan (`AddApplicationLayer()`), so implementing the interface *is* the registration (no explicit list to forget). **New reads and writes belong here, not in a ViewModel** — every display VM was migrated off `DbContext` during the arc and now dispatches commands/queries only. Queries are read-only `AsNoTracking()` SQL projections to custom read-model records (not `Include` graphs — the heavy lifting stays in SQL, only displayed scalars cross the wire). Authoritative wording: `docs/BACKEND-REFACTOR-DESIGN.md` (conventions C1–C12) + `ARCHITECTURE.md`.
- **`BookTracker.Shared\`** — Wire-format DTO records (`CatalogSnapshot`, `BookSnapshot`, `EditionSnapshot`, `WorkSnapshot`, `AuthorSnapshot`, `SeriesSnapshot`) under `Catalog\`. Zero EF dependency so Mobile can reference them without dragging in `BookTracker.Data`. Web projects from EF into these; Mobile + the PWA `/bookshop` deserialise from the same shapes.
- **`BookTracker.Mobile\`** — .NET MAUI Android app (Bookshelf), `net10.0-android`. Pages under `Pages\`. Auth via MSAL public-client against the same Entra app reg as Bookcase's Easy Auth.
- **`BookTracker.Mobile.Cache\`** — sqlite-net-pcl-backed `CatalogCache` library, pure `net10.0`. Exposes `ICatalogCache` with `PopulateAsync` / `ApplyDeltaAsync` / `LookupByIsbn` / `LookupByAuthor` / `SearchAuthors` / `SearchBooksByTitle` / `GetSeriesGaps` / `GetBookEnrichedDetail` / `EnsureCoverCached` / `GetMeta`. Pure-net10 so it tests cleanly without the MAUI runtime.
- **`BookTracker.Mobile.Cache.Tests\`** + **`BookTracker.Tests\`** — xUnit test projects (see Tests section below).
- **`BookTracker.Tools.SnapshotDump\`** — Console `Exe` (not part of the running app) that dumps the full catalog to a JSON snapshot for offline analysis. Reads `ConnectionStrings__DefaultConnection` from the environment; `--out <path>` / `--source <label>` flags; driven by `infra\dump-prod-to-json.ps1`.

When an arc changes the solution shape (a project, a cross-cutting pattern) update this section in the arc's closing PR — same rule as `ARCHITECTURE.md`.

**DbContext lifetime:** Blazor Server circuits are long-lived while `DbContext` is scoped and not thread-safe. `ProgramSetup.cs` registers `AddDbContextFactory<BookTrackerDbContext>`; the **Application** layer's handlers inject `IDbContextFactory<T>` and create/dispose a context per operation (`await using var db = await DbFactory.CreateDbContextAsync();`). ViewModels never touch `DbContext` directly — they go through `IDispatcher` (see `BookTracker.Application`). Do **not** switch back to `AddDbContext` + direct injection.

Entity model: `Book` (Title, `BookStatus`, Rating, Notes, DateAdded, DefaultCoverArtUrl, **UpdatedAt** for delta-sync, **DeletedAt** for soft-delete, optional `Series` + SeriesOrder, many-to-many `Works`, one-to-many `Editions`, many-to-many `Tags`), `Work` (Title, Subtitle, AuthorId/Author, FirstPublishedDate, many-to-many `Genres`, many-to-many `Books`), `Author` (Name unique, nullable self-FK `CanonicalAuthorId` for pen-name aliases, one-to-many `Works`), `Edition` (filtered-unique nullable Isbn, `BookFormat`, DatePrinted, CoverUrl, Publisher, one-to-many `Copies`), `Copy` (Condition, DateAcquired, Notes), `Genre` (hierarchical with parent/child, many-to-many to Work), `Series` (Name, Author string display-only, `SeriesType` Series/Collection, ExpectedCount, one-to-many `Books`), `Tag` (many-to-many with Book), `Publisher`, and `WishlistItem` (Title, Author, Priority, optional Isbn/Series link). The Work refactor sits the abstract creative unit (story / novel / play) above the physical Book grouping — single-Work books are the common case (Add page auto-creates one alongside the Book); compendiums attach extra Works via the Book Detail "Add Work" dialog (the `/books/{id}/edit` page was decommissioned). Authorship, subtitle, and genres live on the Work; series membership lives on the **Book** (the book is installment N of a publication series — a series of short-story-collection books is unmanageable at Work grain, so series moved Work→Book in TODO #56). Pen names: each Work points at a specific Author row; aliases (e.g. Bachman) carry `CanonicalAuthorId` referencing the canonical (King) so aggregations roll up; `/authors` page manages aliases. Find-or-create on save is auto via `AuthorResolver`.

`Book.UpdatedAt` is bumped automatically on every aggregate change by `BookUpdatedAtInterceptor` (a `SaveChangesInterceptor`). A value converter pins `Kind=Utc` on read so cross-timezone clients round-trip the watermark correctly. `Book.DeletedAt` drives soft-delete via a global EF query filter — tombstoned rows are hidden from every normal query, but visible to `IgnoreQueryFilters()` so the catalog snapshot can emit them in `deletedIds[]`. See `ARCHITECTURE.md` for the full surface.

Config convention: connection string name is **`DefaultConnection`**. Dev value lives in `appsettings.Development.json` and points at the Docker SQL container. Prod and staging slots in Azure point at **separate databases** (`booktracker` vs `booktracker-staging` on the same SQL server) via slot-sticky `DefaultConnection` — the slot swap is purely code-shaped and never moves the DB underneath the bits. `appsettings*.json` is **gitignored**; committed templates live alongside them as `appsettings.Example.json` and `appsettings.Development.Example.json` — on a fresh clone, copy each `.Example.json` to the real filename and fill in secrets.

## Branching

**Never commit or push to `main`.** All changes go through a feature branch + PR. Branch-name prefixes: `feat/`, `fix/`, `chore/`, `docs/`, `refactor/` (and `agent/<issue>-<slug>` for the orchestrator's branches). Large features may use sub-branches (`feat/parent/sub-branch`) that PR into the parent feature branch before the parent PRs into `main`. Squash-merge into `main`. See `CONTRIBUTING.md` for full details.

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

**Building and installing Bookshelf.** `BookTracker.Mobile` is outside `BookTracker.slnx` on purpose (see the comment in the solution file), so the main CI job never compiles it; `.github/workflows/mobile-build.yml` does, as the required `mobile-build` check, whenever a PR touches `BookTracker.Mobile/`, `BookTracker.Mobile.Cache/`, `BookTracker.Shared/` or `Directory.Build.props`. Drew's own install path is still `dotnet build -t:Run -f net10.0-android` over USB. For a review install without the laptop, run `.github/workflows/bookshelf-apk.yml` from the Actions tab on the branch in question and approve the run when GitHub asks (the job sits in the `bookshelf-apk` environment, which needs Drew's approval, holds the secrets below as environment secrets, and signs in to Azure as `booktracker-apk-upload`, an identity that can only write to the `bookshelf-builds` container): it builds a Debug APK signed with Drew's debug keystore (restored from the `BOOKSHELF_DEBUG_KEYSTORE_B64` secret, so it installs over the USB build and the AAD redirect hash still matches), with `AppConfig.cs` written from the environment secrets `BOOKSHELF_TENANT_ID`, `BOOKSHELF_MOBILE_CLIENT_ID` and `BOOKSHELF_API_SCOPE` (secrets rather than variables so a public Actions log never shows them; the compile check uses the example file's placeholders instead), uploads it to the private `bookshelf-builds` container on the covers storage account and names the blob in the job summary. It publishes no link and no artifact (the repo's Actions pages are public); Drew downloads the blob with his own Azure login from the portal. The orchestrator asks for that only for a *major* Bookshelf change (a new page or tab, navigation, the sync or cache schema, auth): it labels the PR `needs-drew` after review and waits for a yes; minor changes merge without it.

## AI integration

Three AI providers supported in code, selectable at runtime via toggle on the AI Assistant and Bulk Add pages:

- **Anthropic** (direct public API) — Claude Sonnet for fast ops, Opus for deep analysis. Provisioned in prod when `-AnthropicApiKey` is supplied to `deploy.ps1`.
- **Microsoft Foundry** (Claude on Azure) — supported in code but **not currently provisioned**: the project's Azure subscription is `Sponsored`, which Microsoft excludes from Claude eligibility on Foundry. See `infra/README.md` and `TODO.md`.
- **Azure OpenAI** — GPT-4o. Provisioned automatically in `eastus2` (with a Private Endpoint and KV-stored key) by `infra/modules/ai-services.bicep`.

Config under `AI:` section in appsettings. Only providers with valid config are available; the picker auto-detects. In prod, secret values resolve via Key Vault references — see `infra/README.md` for the wiring.

## Cover storage

Book cover images are mirrored from upstream providers (Open Library, Google Books, Trove) into Azure Blob Storage so renders never depend on upstream latency. `IBookCoverStorage` (`BookTracker.Web/Services/Covers/`) downloads the upstream URL (only an allowed raster content type — JPEG, PNG, GIF, WebP — at most 10 MiB and within the client timeout; anything else is skipped with a logged warning and the row keeps its upstream URL), normalises via SkiaSharp (JPEG, max 1200px on the long edge — an allowed type that fails to decode falls back to raw bytes with a logged warning per Drew's call; same imaging library as the Bookshelf cover cache, replacing SixLabors.ImageSharp after it moved to a build-time licence key in v4), uploads to the `book-covers` container, and the URL stored on `Edition.CoverUrl` / `Book.DefaultCoverArtUrl` swaps to the blob URL.

`CoverMirrorBackgroundService` polls every 30s for un-mirrored URLs and processes them in batches of 50. Same service handles both initial backfill (legacy upstream URLs in the DB before this shipped) and ongoing mirroring of newly-added covers — there's no save-site integration in PR1 (PR2 will move new-cover mirroring inline at the save site so the polling becomes backfill-only).

Local: Azurite emulator on `localhost:10000` (see Docker Compose section above). Connection string + endpoint URL are in `appsettings.Development.Example.json` using Azurite's well-known dev account name. Prod: real Storage Account provisioned by `infra/modules/cover-storage.bicep`, connection string lives in Key Vault as `CoverStorageConnectionString` and resolves into the `CoverStorage:ConnectionString` app setting via a KV reference.

## Tests

Two xUnit projects:

- **`BookTracker.Tests\`** — Bookcase tests. xUnit + NSubstitute against a **real SQL Server via MSSQL Testcontainer** (one container per process, Respawn-based wipe + reseed per `TestDbContextFactory`). EF InMemory was the original choice; the pivot off it caught real bugs InMemory's lax SQL translation had let through. Playwright E2E lives in `E2E/` (gated by `Category=E2E`) — requires `ms-playwright` browsers locally.
- **`BookTracker.Mobile.Cache.Tests\`** — Bookshelf cache tests. Pure xUnit + real SQLite files (GUID-named temp files, one per test). No MAUI runtime needed.

CI runs `dotnet test` on all PRs to main. The full suite must stay green before merge. The Integration category needs Docker for Testcontainers, so a headless orchestrator run (above) covers Unit and Component locally and relies on the CI job for Integration; the gate report on its PRs says so.

## Blog

Posts in `blog/` follow the shared syndication contract: [`drew-blog/docs/BLOG_POST_CONTRACT.md`](https://github.com/N3rdage/drew-blog/blob/main/docs/BLOG_POST_CONTRACT.md) (maintained there — link it, don't copy it). They syndicate to `silly.ninja` under `/library/{slug}/`, and the aggregator's sync **validates and decorates — it never repairs**, so a non-compliant post fails the sync loudly. Load-bearing rules:

- **Filenames + slugs are frozen after publish** — `YYYY-MM-DD-NN-slug.md`; site URLs and inbound cross-links hang off them. Never rename a post or change a slug.
- **Frontmatter requires** `title`, `description`, `date`, `author`, `reviewed_by`, `slug`, `tags`. `date` == filename date; `slug` == filename remainder after `YYYY-MM-DD-NN-`; `tags` is a flat lowercase list. `description` is 1–2 plain-text sentences (no markdown) feeding listings, RSS, and OG cards.
- **No H1 in the body** — the site layout renders the frontmatter title; body headings start at `##`.
- **Sibling-post links are relative** — `[text](./YYYY-MM-DD-NN-slug.md)`, so they work on GitHub *and* get rewritten to `/library/{slug}/` by the sync. Links to non-post targets (repo root, code files, external sites, the `blog/` directory itself) stay absolute.
- **Images** are relative (`./images/…` or `images/…`) with alt text; the sync copies them.

`blog/sources/` (retros, backlogs, raw material) is ignored by the sync — it's mining input for posts, not published content. A push to `main` touching `blog/**` fires `.github/workflows/notify-drew-blog.yml`, which pings drew-blog to re-sync (no-ops safely until the `DREW_BLOG_DISPATCH_TOKEN` secret exists).

## Session memory

Durable cross-session memory — feedback rules, arc retros, runbooks, project context — lives in the git-tracked **`.claude-memory\`** directory, indexed by `.claude-memory\MEMORY.md`. Claude Code's auto-memory loads that index at the start of every session and reads individual topic files on demand.

**Do NOT add an `@.claude-memory/MEMORY.md` import (or any `@` import of the index) to this file.** Auto-memory already loads the index every session via the symlink below, so an import double-loads the ~16 KB file. The import *looks* missing only because the symlink does its work invisibly — a 2026-07 external review proposed adding exactly this before catching the duplication. Full story: `retros/retro_config_memory_cleanup.md`.

The load works because auto-memory's per-project directory (`~\.claude\projects\<repo-slug>\memory`) is a symlink to the in-repo `.claude-memory\`. On a fresh machine, recreate that link once with `scripts\link-claude-memory.ps1`; without it, auto-memory reads/writes a machine-local directory and the committed memory neither loads nor captures new notes into the repo.
