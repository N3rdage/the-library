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

Entity model: `Book` (Title, Author, many-to-many `Genres`, `BookStatus` enum Unread/Reading/Read, Rating 0–5, Notes, DateAdded, DefaultCoverArtUrl, one-to-many `Copies`), `BookCopy` (Isbn, `BookFormat`, DatePrinted, `BookCondition` default Good, optional CustomCoverArtUrl), `Genre` (unique Name), and `WishlistItem` (Title, Author, `WishlistPriority`, nullable Price `decimal(10,2)`, DateAdded).

Config convention: connection string name is **`DefaultConnection`**. Dev value lives in `appsettings.Development.json` and points at the Docker SQL container. Prod value comes from Azure App Service configuration. `appsettings*.json` is **gitignored**; committed templates live alongside them as `appsettings.Example.json` and `appsettings.Development.Example.json` — on a fresh clone, copy each `.Example.json` to the real filename and fill in secrets.

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

## Local SQL Server via Docker Desktop

`docker-compose.yml` at the repo root runs SQL Server 2022 Developer on `localhost:1433`. SA password defaults to `BookTracker!Dev1` (override with the `MSSQL_SA_PASSWORD` env var via `$env:MSSQL_SA_PASSWORD = "..."` before `docker compose up`). Data persists in the `booktracker-db-data` named Docker volume. The dev connection string in `appsettings.Development.json` already targets this container.

```powershell
docker compose up -d        # start
docker compose down         # stop (add -v to wipe the volume)
```

Typical first-run loop: `docker compose up -d` → `dotnet ef database update ...` → `dotnet run --project .\BookTracker.Web`.

## Not yet in the repo

No test project, no CI workflow, no Anthropic integration. When adding tests, create a sibling `BookTracker.Tests\` project and add it to `BookTracker.slnx`.
