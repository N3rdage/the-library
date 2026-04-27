# Architecture

This document describes the overall design and structure of BookTracker. It should be kept up to date as the system evolves.

## Overview

BookTracker is an ASP.NET Core Blazor Web App for managing a personal book library. It runs in **Interactive Server** render mode (Blazor Server), backed by EF Core + SQL Server. It includes AI-powered features via multiple providers: Anthropic (Claude, public API) and Azure OpenAI (GPT-4o, Azure-hosted). Microsoft Foundry (Claude on Azure) is supported in code but not currently provisioned — see `infra/README.md` and `TODO.md`.

Target deployment: Azure App Service + Azure SQL (Basic tier) via GitHub Actions. SQL, Key Vault, and Azure OpenAI sit behind Private Endpoints; the App Service reaches them through VNet integration + a peered eastus2 VNet for the OpenAI account.

## Solution structure

```
BookTracker.slnx
  BookTracker.Data/        Class library — entities, DbContext, EF migrations
  BookTracker.Web/         Blazor Web App — UI, services, ViewModels
  BookTracker.Tests/       xUnit test project
```

All projects target `net10.0`.

## Data model

```
Book                                                ← physical-object grouping
  Title, Category (Fiction/NonFiction),
  Status (Unread/Reading/Read), Rating (0-5),
  Notes, DateAdded, DefaultCoverArtUrl
  ├── Works (many-to-many)                          ← what's actually inside the book
  │     Title, Subtitle, FirstPublishedDate
  │     ├── Author (many-to-1 → Author)
  │     ├── Genres (many-to-many → Genre)
  │     └── Series (many-to-1, optional) + SeriesOrder
  ├── Editions (1-to-many)
  │     ISBN (filtered unique, nullable for pre-ISBN books),
  │     Format, DatePrinted, CoverUrl
  │     ├── Copies (1-to-many)
  │     │     Condition (AsNew..Poor), DateAcquired, Notes
  │     └── Publisher (many-to-1, optional)
  └── Tags (many-to-many, e.g. "follow-up")

Series
  Name (unique), Author (string, optional, display-only),
  Type (Series/Collection),
  ExpectedCount (for numbered series), Description
  └── Works (1-to-many)

Author
  Name (unique)
  CanonicalAuthorId (nullable self-FK — pen name aliases)
  └── Aliases (1-to-many — inverse of CanonicalAuthorId)
  └── Works (1-to-many)

Genre
  Name (unique), ParentGenre (self-referential)
  └── Children (1-to-many)
  └── Works (many-to-many)

Publisher
  Name (unique)
  └── Editions (1-to-many)

Tag
  Name (unique)
  └── Books (many-to-many)

WishlistItem
  Title, Author, Priority (Low/Medium/High), Price,
  ISBN (optional), Series (optional FK), SeriesOrder

IgnoredDuplicate
  EntityType (Author/Work/Book/Edition), LowerId, HigherId,
  DismissedAt, Note — records user-dismissed duplicate pairs for /duplicates.
  Polymorphic across four entity types (no FK); orphaned rows swept on detect.

GenreSeed
  Static seed data — 48+ curated genres in a hierarchical taxonomy
```

Key design decisions:
- **Work vs Book vs Edition vs Copy**: A `Work` is the abstract creative unit (a story / novel / play / poem). A `Book` is the physical-object grouping containing one or more Works (a compendium contains many; a novel contains one). An `Edition` is a specific printing of the Book with a unique ISBN. A `Copy` is the physical item you own. Authorship, subtitle, genres, and series membership belong to the Work, not the Book — a Christie short-story collection is "horror" because each contained story is, not because the volume itself is tagged.
- **Author entity with self-referential pen names**: each `Work` points at a specific `Author` row. Pen names (Richard Bachman) get their own Author row with `CanonicalAuthorId` pointing at the canonical (Stephen King). A Bachman novel is *displayed* as "by Richard Bachman" (the Work's `AuthorId` points at Bachman) but aggregations group by `CanonicalAuthorId ?? Id` so King's tally includes the Bachman titles. Find-or-create on save is auto (typing a fresh name silently creates a canonical Author); merging duplicates and managing aliases happens on `/authors`.
- **Single-Work books are the common case** — the Add page auto-creates one Work alongside the Book and mirrors the title between them. Compendium support (multiple Works per Book) lives on the Edit page's "Other works" section.
- **Edition.ISBN is unique** (filtered — pre-1974 books with no ISBN coexist as nullable rows).
- **Series.Type**: `Series` = numbered with known order; `Collection` = loose grouping. Series membership is per-Work — a short story republished in three compendiums shows once in the series with three book references.
- **Genre hierarchy**: top-level genres with sub-genres. Selecting a sub-genre auto-selects its parent.

## Architecture pattern — MVVM

The app follows an MVVM (Model-View-ViewModel) pattern:

```
[Razor Component (.razor)]  ←→  [ViewModel (.cs)]  ←→  [DbContext / Services]
     View (UI binding)           State + Logic           Data + External APIs
```

- **ViewModels** are plain C# classes under `BookTracker.Web/ViewModels/`. They hold all state and business logic.
- **Razor components** are thin views that inject a VM and bind to its properties. They handle only UI concerns: JS interop, navigation, Blazor lifecycle.
- **Services** handle external concerns: ISBN lookup (Open Library / Google Books / Trove), AI features (Anthropic API), series matching.

VM lifetime:
- **Transient**: most VMs — each component instance gets its own.
- **Scoped**: `AIAssistantViewModel` and `IAIAssistantService` — shared across the Blazor circuit so the call counter and prompt cache persist per session.

## DbContext lifetime

Blazor Server circuits are long-lived while `DbContext` is scoped and not thread-safe. The app uses `IDbContextFactory<BookTrackerDbContext>`:

```csharp
await using var db = await dbFactory.CreateDbContextAsync();
```

A short-lived context is created per operation. Never inject `DbContext` directly.

## Pages and routes

| Route | Page | Purpose |
|-------|------|---------|
| `/` | Home | Dashboard — book count, author/genre stats |
| `/books` | Library | Filterable book list (search, category, genre, tag, author). Group-by picker (Author / Genre / Series / None) renders the books as a collapsible accordion of groups, each with its own paginated book list (lazy-loaded on first expand). Filters reduce within groups. Desktop table + mobile cards. |
| `/books/add` | Add Book | ISBN lookup + manual entry. Creates Book + Edition + Copy. Series suggestion after lookup. |
| `/books/{id}` | Book Detail | Default browsing surface for a single book. Read-only scaffold with inline auto-save (rating / status / notes / tags). Modal edits open `BookEditDialog` / `WorkEditDialog` / `EditionFormDialog` / `CopyFormDialog`. |
| `/books/{id}/edit` | Edit Book | Full-edit escape hatch. Edit metadata, genres, tags, series assignment. Manage editions and copies. Delete book. |
| `/books/bulk-add` | Bulk Add | Rapid ISBN entry (text or barcode scanner). Discovery grid with async lookup, accept/follow-up, duplicate detection. |
| `/series` | Series List | All series/collections with completion status |
| `/series/new` | New Series | Create a new series or collection |
| `/series/{id}` | Edit Series | Edit series, manage books in series, reorder |
| `/shopping` | Shopping Mode | Mobile-optimised. "Do I have this?" (scan/search), series gaps, shopping list with "bought" action. |
| `/assistant` | AI Assistant | Book advisor (Opus), genre cleanup, collection cataloguing, shopping suggestions (Sonnet). |
| `/authors` | Authors | MudBlazor list with per-row drill-down to Works/Books, alias rollup on canonical rows, inline rename / merge / alias-resolve. Deep-linked from Home top-10. |
| `/publishers` | Publishers | MudBlazor list mirroring `/authors` structurally — per-row drill-down to editions, inline rename, two-step-confirm merge (no alias model — outright absorption), delete-unused. |
| `/duplicates` | Duplicates | Tabs for Authors / Works / Books / Editions. Lists candidate duplicate pairs detected on-demand. Dismiss false positives (reversible via the "Dismissed" section). Author pairs have a Merge → button. Web-primary, desktop-first layout. |
| `/duplicates/merge/author/{idA}/{idB}` | Merge authors | Side-by-side review of the pair, radio to pick a winner, preview of impact (N works + M aliases to reassign), transactional merge. Refuses when the two authors resolve to different canonicals — user resolves aliases on `/authors` first. |
| `/duplicates/merge/work/{idA}/{idB}` | Merge works | Side-by-side review, radio to pick a winner, preview of impact (books to reassign + any books that already contain both → loser dropped). Transactional with auto-fill-empties semantics. Refuses if the two resolve to different authors (merge authors first). |
| `/duplicates/merge/edition/{idA}/{idB}` | Merge editions | Side-by-side review with cover thumbnails, winner radio, preview of copies to reassign + which empty winner fields will be auto-filled from loser (ISBN, date printed, publisher, cover). Refuses cross-book edition merges (merge the Books first). |
| `/duplicates/merge/book/{idA}/{idB}` | Merge books | Side-by-side review with cover thumbnail, winner radio, preview of editions to reassign + works / tags to union + auto-fill hints (notes, cover, rating-if-unrated). No structural incompatibility path — Book merge is the aggregator and everything beneath it is moved or unioned. |

## Shared components

| Component | Purpose |
|-----------|---------|
| `BookForm.razor` | Book metadata fields (title, author, category, status, rating, notes, cover URL) + cover preview |
| `GenrePicker.razor` | Hierarchical genre checkbox grid with fuzzy matching from ISBN lookup |
| `EditionCopyForm.razor` | Edition fields (ISBN, format, publisher, date, cover) + copy condition |
| `AIProviderToggle.razor` | Dropdown to switch AI provider at runtime + call counter badge |

## Services

### BookLookupService
Looks up book metadata by ISBN. Tries Open Library first, falls back to Google Books, then Trove (NLA) as a coverage-of-last-resort for self-published / Australian titles the other two tend to miss. Trove is skipped silently when no API key is configured. Returns `BookLookupResult` with title, author, publisher, genres, cover URL, etc.

### DuplicateDetectionService
Scans the library for candidate duplicate pairs across Authors, Works, Books, and Editions. Authors match on either normalised full name *or* shared surname + first-name initial (so "Doug Preston" / "Douglas Preston" / "D Preston" all surface together). Works, Books, and Editions use exact-after-normalisation. Dismissed pairs are persisted in `IgnoredDuplicate` (polymorphic table, unique on `(EntityType, LowerId, HigherId)`) and orphaned rows are swept on each run. Returns a `DuplicateReport`.

### AuthorMergeService
Merges two Author rows after user review. Refuses when the two authors resolve to different canonicals (user must resolve aliases on `/authors` first). Otherwise runs in one transaction: reassigns `Work.AuthorId`, reassigns external aliases' `CanonicalAuthorId`, clears any `IgnoredDuplicate` rows mentioning the loser, deletes the loser. One edge case: when the winner is itself an alias of the loser, winner is promoted to canonical before the delete so its `CanonicalAuthorId` doesn't dangle. Returns a result with reassignment counts + a flag for the promotion case.

### WorkMergeService
Merges two Work rows after user review. **Auto-fill-empties** semantics: any winner field that's null/empty gets taken from loser (Subtitle, FirstPublishedDate+Precision pair, SeriesId+SeriesOrder pair); Genres are unioned. Fields the winner already has are preserved. The VM's `EnrichmentHints` surfaces exactly what will move so the user can override (by editing the winner) before confirming. Refuses if the two Works have different `AuthorId`. Transactional: for each Book attached to the loser, adds the winner if not already present then clears `loser.Books`, which deletes the `BookWork` rows; clears any `IgnoredDuplicate` referencing the loser; deletes the loser Work. The "Book contains both" count is surfaced in preview + result so the UI can flag that those Books will just lose the loser attachment (winner stays).

### EditionMergeService
Merges two Edition rows belonging to the same Book after user review. Same shape as WorkMergeService: transactional, auto-fill-empties (ISBN, DatePrinted+Precision pair, CoverUrl, PublisherId), reassigns `Copy.EditionId`, clears stale `IgnoredDuplicate` rows, deletes the loser Edition. Refuses cross-Book merges (if the Editions are really the same, the Books themselves are duplicates and the Book-level merge should happen first).

### BookMergeService
Merges two Book rows. Reassigns Editions (which carry their Copies) via `Edition.BookId`, unions Works and Tags (dedup by ID), auto-fills empty winner fields (Notes, DefaultCoverArtUrl, Rating — where `Rating == 0` is treated as "unrated" since the stars-1-to-5 UI can't produce an active 0). Transactional; clears stale `IgnoredDuplicate` rows; deletes the loser Book. No structural incompatibility path — the Edition unique-ISBN index is global so two Books can never hold editions with overlapping non-null ISBNs. Any resulting no-ISBN Edition duplicates (same format/publisher/date) surface on `/duplicates` for separate cleanup.

### WorkSearchService
Typeahead-style substring search across Works. Min 2 chars; case-insensitive via `ToLower()` on both sides (keeps InMemory tests honest). Ranks starts-with matches above contains-anywhere, alphabetical within. `excludeBookId` filter lets the Edit Book "Attach existing Work" UI exclude Works already attached. Returns up to `maxResults` results (default 20).

### SeriesMatchService
Local series detection after ISBN lookup. Strategies:
1. Author already has a series — suggests it
2. Author has multiple series — matches by title
3. Author has 2+ ungrouped books — suggests creating a collection
4. Title contains series indicators (Book #, Vol., Part II)

### AI Assistant (IAIAssistantService)
Multi-provider architecture with runtime switching via `AIProviderFactory`.

**Providers:**
- `AnthropicAIAssistantService` — Anthropic API direct. Sonnet for fast ops, Opus for deep analysis. Prompt caching via `CacheControlEphemeral`.
- `MicrosoftFoundryAIAssistantService` — Claude via Microsoft Foundry. Same Anthropic SDK with custom endpoint. Uses Azure credits.
- `AzureOpenAIAssistantService` — GPT-4o via Azure OpenAI SDK. Single deployment for all operations.

**Shared logic:** `SharedParsers` provides JSON parsing and prompt building used by all providers.

| Method | Speed | Purpose |
|--------|-------|---------|
| `ExtractIsbnFromImageAsync` | Fast | OCR ISBN from photo (vision API) |
| `SuggestGenresAsync` | Fast | Suggest genres from preset taxonomy |
| `SuggestCollectionsAsync` | Fast | Suggest series/collection groupings |
| `SuggestShoppingListAsync` | Fast | Recommend books based on library patterns |
| `AssessBookAsync` | Deep | Suitability assessment for a book/author |

### AIProviderFactory
Scoped factory managing provider lifecycle. Auto-detects available providers from config (checks for API keys). Supports runtime switching via `SwitchProvider()`.

## ISBN capture

### Barcode scanning
ISBN barcodes (EAN-13/EAN-8) are scanned via the `html5-qrcode` library (v2.3.8, static JS under `wwwroot/lib/`). A JS interop wrapper (`barcode-scanner.js`) manages the scanner lifecycle with 3-second debounce on duplicate scans. The scanning viewport is optimised for 1D barcodes (wide and short, `aspectRatio: 2.0`). Used on Bulk Add and Shopping pages.

### Photo ISBN OCR
For older books without barcodes, `photo-capture.js` opens the camera for a still photo. The image is scaled to max 800px, compressed to 70% JPEG, and sent to the active AI provider's `ExtractIsbnFromImageAsync` for vision OCR. SignalR max message size increased to 512KB for image transfer.

## UI component library

Mid-migration from Bootstrap to **MudBlazor 9** (`BookTracker.Web.csproj`). Current state:

- **Pages using MudBlazor:** Home, Duplicates/MergeBook, Book Detail (`/books/{id}`) and its dialogs (BookEditDialog, WorkEditDialog, EditionFormDialog, CopyFormDialog), the `MudGenrePicker` shared component, Authors, Publishers. These use `MudCard`, `MudButton`, `MudText`, `MudContainer`, `MudDialog`, `MudAutocomplete`, etc.
- **Pages still on Bootstrap:** Library list, Book Add, Book Bulk Add, Book Edit (the "full edit page" escape hatch), Series, Shopping, AI Assistant, Duplicates list. The navbar in `MainLayout.razor` is still Bootstrap.
- **Rollout strategy:** no migration deadline — pages convert as they're touched for other reasons. Low-traffic pages may stay Bootstrap indefinitely; that's fine.
- **Coexistence:** both stylesheets are loaded in `App.razor`. MudBlazor's four root providers (`MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`) sit in `MainLayout.razor` — harmless on Bootstrap-only pages. Each page picks one lane.
- **Theme:** custom "warm library" palette in `BookTracker.Web/Theme/BookTrackerTheme.cs` (oxblood / antique brass / forest / parchment / espresso). Applied globally via `<MudThemeProvider Theme="BookTrackerTheme.Default" />`. Dark mode not wired yet.

## Mobile responsiveness

Key mobile workflows: barcode scanning, library search, shopping mode. Pages use Bootstrap responsive utilities:
- **Collapsible filters** on Library list (below `md` breakpoint)
- **Card layouts** replacing tables on mobile (Library list, Bulk Add discovery grid, Series list)
- **Scanner-first** on mobile (full-width primary button above text input)

## Testing

xUnit + NSubstitute + EF Core InMemory provider. Tests cover ViewModels and services — business logic that can break silently. Pure markup/CSS changes don't need tests.

Test helper: `TestDbContextFactory` creates isolated in-memory databases per test.

CI runs `dotnet test` on all PRs to main.

## Configuration

| Setting | Source | Purpose |
|---------|--------|---------|
| `ConnectionStrings:DefaultConnection` | appsettings / Azure config | SQL Server connection (AAD via managed identity in Azure). In Azure: **slot-sticky**; prod slot reads `booktracker`, staging slot reads `booktracker-staging`, and a slot swap does not move the CS — see `infra/README.md`. |
| `AI:DefaultProvider` | Azure App Setting | `Anthropic`, `MicrosoftFoundry`, or `AzureOpenAI` |
| `AI:Anthropic:ApiKey` | Key Vault ref (prod) / appsettings (dev) | Anthropic API key |
| `AI:Anthropic:FastModel` | appsettings (default: SDK constant) | Model for fast AI ops |
| `AI:Anthropic:DeepModel` | appsettings (default: SDK constant) | Model for deep analysis |
| `AI:MicrosoftFoundry:Endpoint` | _(not set in prod — provider deferred)_ | Microsoft Foundry endpoint URL |
| `AI:MicrosoftFoundry:ApiKey` | _(not set in prod — provider deferred)_ | Microsoft Foundry key |
| `AI:MicrosoftFoundry:FastDeployment` | _(not set in prod — provider deferred)_ | Deployment for fast ops |
| `AI:MicrosoftFoundry:DeepDeployment` | _(not set in prod — provider deferred)_ | Deployment for deep analysis |
| `AI:AzureOpenAI:Endpoint` | Bicep output | Azure OpenAI endpoint URL |
| `AI:AzureOpenAI:ApiKey` | Key Vault ref (written by Bicep via `listKeys()`) | Azure OpenAI key |
| `AI:AzureOpenAI:Deployment` | Bicep output | GPT-4o deployment name |
| `Trove:ApiKey` | Key Vault ref (prod) / appsettings (dev) | National Library of Australia API key — optional third-line ISBN lookup provider |

In prod, secret App Settings resolve via `@Microsoft.KeyVault(SecretUri=…)` references — the App Service's managed identity has `Key Vault Secrets User` on the vault. `deploy.ps1` writes `AuthClientSecret` + optionally `AIAnthropicApiKey` and `TroveApiKey` to the vault; `ai-services.bicep` writes `AIAzureOpenAIApiKey` via `listKeys()` against the newly-created OpenAI account.

Dev config templates: `appsettings.Example.json`, `appsettings.Development.Example.json`. Copy and fill in secrets. Only configure providers you want to use — the toggle auto-detects available providers.

## Infrastructure

- Docker Compose for local SQL Server 2022 Developer
- GitHub Actions CI (build + test on PR)
- Dependabot for NuGet (EF Core grouped) and npm (html5-qrcode)
- Azure Bicep templates under `infra/`. SQL, Key Vault, and Azure OpenAI are reachable only via Private Endpoints; the App Service uses VNet integration + a peered eastus2 VNet to reach OpenAI. See `infra/README.md` for the full topology.
- Auto-migration on startup (`TODO.md`: switch to deploy-time migration bundle when going multi-instance)

## Progressive Web App (PWA)

The app is installable on mobile (iOS Safari, Android Chrome) and desktop (Edge, Chrome) as a PWA — home-screen icon, standalone display (no browser chrome), theme-colour integration. Wired up in `App.razor` with a standard `manifest.webmanifest` + icons + apple-touch meta tags.

Interactive Server mode means the app needs a live SignalR connection to function — the service worker is there for asset caching (faster repeat loads), not offline use. Strategy is network-first with cache fallback for same-origin GETs; `/_blazor/*` passes through untouched. Cache version bumps on `service-worker.js` invalidate old caches cleanly.

Icons live under `wwwroot/icons/`; regenerate them from the script in `scripts/generate-pwa-icons.ps1` (System.Drawing-based so no external tools required). The SVG master is at `wwwroot/icons/icon.svg`.

## Key conventions

- **Branching**: never commit to main. Feature branches with `feat/`, `fix/`, `chore/`, `docs/`, `refactor/` prefixes. Squash-merge.
- **Planning**: every feature gets a plan first. PRs split by concern for medium+ complexity.
- **Performance target**: system must handle 3,000+ book copies.
- **Deployment safety**: migrations must retain data. Breaking changes need defaults.
- **Mobile priority**: every new feature should clarify if mobile+web or web-only.
