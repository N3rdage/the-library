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
  │     Title, Subtitle, Author, FirstPublishedDate
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
  Name (unique), Author (optional), Type (Series/Collection),
  ExpectedCount (for numbered series), Description
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

GenreSeed
  Static seed data — 48+ curated genres in a hierarchical taxonomy
```

Key design decisions:
- **Work vs Book vs Edition vs Copy**: A `Work` is the abstract creative unit (a story / novel / play / poem). A `Book` is the physical-object grouping containing one or more Works (a compendium contains many; a novel contains one). An `Edition` is a specific printing of the Book with a unique ISBN. A `Copy` is the physical item you own. Authorship, subtitle, genres, and series membership belong to the Work, not the Book — a Christie short-story collection is "horror" because each contained story is, not because the volume itself is tagged.
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
- **Services** handle external concerns: ISBN lookup (Open Library / Google Books), AI features (Anthropic API), series matching.

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
| `/books` | Library | Filterable book list (search, category, genre, tag, author). Desktop table + mobile cards. |
| `/books/add` | Add Book | ISBN lookup + manual entry. Creates Book + Edition + Copy. Series suggestion after lookup. |
| `/books/{id}/edit` | Edit Book | Edit metadata, genres, tags, series assignment. Manage editions and copies. Delete book. |
| `/books/bulk-add` | Bulk Add | Rapid ISBN entry (text or barcode scanner). Discovery grid with async lookup, accept/follow-up, duplicate detection. |
| `/series` | Series List | All series/collections with completion status |
| `/series/new` | New Series | Create a new series or collection |
| `/series/{id}` | Edit Series | Edit series, manage books in series, reorder |
| `/shopping` | Shopping Mode | Mobile-optimised. "Do I have this?" (scan/search), series gaps, shopping list with "bought" action. |
| `/assistant` | AI Assistant | Book advisor (Opus), genre cleanup, collection cataloguing, shopping suggestions (Sonnet). |

## Shared components

| Component | Purpose |
|-----------|---------|
| `BookForm.razor` | Book metadata fields (title, author, category, status, rating, notes, cover URL) + cover preview |
| `GenrePicker.razor` | Hierarchical genre checkbox grid with fuzzy matching from ISBN lookup |
| `EditionCopyForm.razor` | Edition fields (ISBN, format, publisher, date, cover) + copy condition |
| `AIProviderToggle.razor` | Dropdown to switch AI provider at runtime + call counter badge |

## Services

### BookLookupService
Looks up book metadata by ISBN. Tries Open Library first, falls back to Google Books. Returns `BookLookupResult` with title, author, publisher, genres, cover URL, etc.

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
| `ConnectionStrings:DefaultConnection` | appsettings / Azure config | SQL Server connection (AAD via managed identity in Azure) |
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

In prod, secret App Settings resolve via `@Microsoft.KeyVault(SecretUri=…)` references — the App Service's managed identity has `Key Vault Secrets User` on the vault. `deploy.ps1` writes `AuthClientSecret` + optionally `AIAnthropicApiKey` to the vault; `ai-services.bicep` writes `AIAzureOpenAIApiKey` via `listKeys()` against the newly-created OpenAI account.

Dev config templates: `appsettings.Example.json`, `appsettings.Development.Example.json`. Copy and fill in secrets. Only configure providers you want to use — the toggle auto-detects available providers.

## Infrastructure

- Docker Compose for local SQL Server 2022 Developer
- GitHub Actions CI (build + test on PR)
- Dependabot for NuGet (EF Core grouped) and npm (html5-qrcode)
- Azure Bicep templates under `infra/`. SQL, Key Vault, and Azure OpenAI are reachable only via Private Endpoints; the App Service uses VNet integration + a peered eastus2 VNet to reach OpenAI. See `infra/README.md` for the full topology.
- Auto-migration on startup (`TODO.md`: switch to deploy-time migration bundle when going multi-instance)

## Key conventions

- **Branching**: never commit to main. Feature branches with `feat/`, `fix/`, `chore/`, `docs/`, `refactor/` prefixes. Squash-merge.
- **Planning**: every feature gets a plan first. PRs split by concern for medium+ complexity.
- **Performance target**: system must handle 3,000+ book copies.
- **Deployment safety**: migrations must retain data. Breaking changes need defaults.
- **Mobile priority**: every new feature should clarify if mobile+web or web-only.
