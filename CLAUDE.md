# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Environment

**Windows only.** All commands in this file and any you suggest must target a Windows shell (PowerShell 7 / Windows PowerShell / `cmd.exe`). Do not use Unix-isms like `/dev/null`, forward-slash-only paths, `export VAR=...`, `&&` chaining habits from bash scripts, or POSIX tools (`grep`, `sed`, `cat`) in instructions. Use `;` to chain in PowerShell, `$env:VAR = "..."` to set env vars, backslashes or quoted forward-slash paths, and `Select-String` / `Get-Content` when a shell tool is needed. The repo path contains a space (`C:\Users\Drew.Work\code\The Library`) — always quote it.

## Project

BookTracker — ASP.NET Core Blazor Web App (net10.0) using **Interactive Server** render mode, backed by EF Core + SQL Server. Target deployment is Azure App Service against Azure SQL (Basic tier) via GitHub Actions. Planned: AI book recommendations via the Anthropic API.

## Architecture

Two-project solution (`BookTracker.slnx`, the new XML solution format):

- **`BookTracker.Web\`** — Blazor Web App host. Owns `Program.cs`, `appsettings*.json`, Razor components under `Components\` (`App.razor`, `Routes.razor`, `Layout\MainLayout.razor`, pages under `Components\Pages\`), and lookup service under `Services\`. The whole app is rendered with `@rendermode="InteractiveServer"` set globally via `<Routes>` in `App.razor` — no per-page render-mode attributes. Uses net9+ `MapStaticAssets()` pipeline.
- **`BookTracker.Data\`** — Class library holding `BookTrackerDbContext`, entities in `Models\`, and EF migrations in `Migrations\`. EF tooling (`Microsoft.EntityFrameworkCore.Tools`) lives here, so migrations run against this project with `BookTracker.Web` as the startup project (so config + connection string resolve).

**DbContext lifetime:** Blazor Server circuits are long-lived while `DbContext` is scoped and not thread-safe. `Program.cs` registers `AddDbContextFactory<BookTrackerDbContext>`; components inject `IDbContextFactory<T>` and create/dispose a context per operation (`await using var db = await DbFactory.CreateDbContextAsync();`). Do **not** switch back to `AddDbContext` + direct injection.

Entity model: `Book` (Title, `BookStatus`, Rating, Notes, DateAdded, DefaultCoverArtUrl, many-to-many `Works`, one-to-many `Editions`, many-to-many `Tags`), `Work` (Title, Subtitle, AuthorId/Author, FirstPublishedDate, many-to-many `Genres`, optional `Series` + SeriesOrder, many-to-many `Books`), `Author` (Name unique, nullable self-FK `CanonicalAuthorId` for pen-name aliases, one-to-many `Works`), `Edition` (filtered-unique nullable Isbn, `BookFormat`, DatePrinted, CoverUrl, Publisher, one-to-many `Copies`), `Copy` (Condition, DateAcquired, Notes), `Genre` (hierarchical with parent/child, many-to-many to Work), `Series` (Name, Author string display-only, `SeriesType` Series/Collection, ExpectedCount, one-to-many `Works`), `Tag` (many-to-many with Book), `Publisher`, and `WishlistItem` (Title, Author, Priority, optional Isbn/Series link). The Work refactor sits the abstract creative unit (story / novel / play) above the physical Book grouping — single-Work books are the common case (Add page auto-creates one alongside the Book); compendiums use the Edit page's "Other works" section. Authorship, subtitle, genres, and series live on the Work, not the Book. Pen names: each Work points at a specific Author row; aliases (e.g. Bachman) carry `CanonicalAuthorId` referencing the canonical (King) so aggregations roll up; `/authors` page manages aliases. Find-or-create on save is auto via `AuthorResolver`.

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

The app is used on both desktop and mobile (phones for barcode scanning and quick library checks). When planning new features, always clarify whether the feature should be **mobile-prioritised** (responsive-first, tested at small breakpoints) or **web-only** (desktop layout sufficient). Key mobile workflows: bulk ISBN scanning, library search.

## AI integration

Three AI providers supported in code, selectable at runtime via toggle on the AI Assistant and Bulk Add pages:

- **Anthropic** (direct public API) — Claude Sonnet for fast ops, Opus for deep analysis. Provisioned in prod when `-AnthropicApiKey` is supplied to `deploy.ps1`.
- **Microsoft Foundry** (Claude on Azure) — supported in code but **not currently provisioned**: the project's Azure subscription is `Sponsored`, which Microsoft excludes from Claude eligibility on Foundry. See `infra/README.md` and `TODO.md`.
- **Azure OpenAI** — GPT-4o. Provisioned automatically in `eastus2` (with a Private Endpoint and KV-stored key) by `infra/modules/ai-services.bicep`.

Config under `AI:` section in appsettings. Only providers with valid config are available; the picker auto-detects. In prod, secret values resolve via Key Vault references — see `infra/README.md` for the wiring.

## Cover storage

Book cover images are mirrored from upstream providers (Open Library, Google Books, Trove) into Azure Blob Storage so renders never depend on upstream latency. `IBookCoverStorage` (`BookTracker.Web/Services/Covers/`) downloads the upstream URL, normalises via ImageSharp (JPEG, max 1200px on the long edge — falls back to raw bytes with a logged warning if conversion fails per Drew's call), uploads to the `book-covers` container, and the URL stored on `Edition.CoverUrl` / `Book.DefaultCoverArtUrl` swaps to the blob URL.

`CoverMirrorBackgroundService` polls every 30s for un-mirrored URLs and processes them in batches of 50. Same service handles both initial backfill (legacy upstream URLs in the DB before this shipped) and ongoing mirroring of newly-added covers — there's no save-site integration in PR1 (PR2 will move new-cover mirroring inline at the save site so the polling becomes backfill-only).

Local: Azurite emulator on `localhost:10000` (see Docker Compose section above). Connection string + endpoint URL are in `appsettings.Development.Example.json` using Azurite's well-known dev account name. Prod: real Storage Account provisioned by `infra/modules/cover-storage.bicep`, connection string lives in Key Vault as `CoverStorageConnectionString` and resolves into the `CoverStorage:ConnectionString` app setting via a KV reference.

## Tests

Tests live in `BookTracker.Tests\` (xUnit + NSubstitute + EF InMemory). CI runs tests on all PRs to main.
