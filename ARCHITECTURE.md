# Architecture

This document describes the overall design and structure of BookTracker. It should be kept up to date as the system evolves.

## Overview

BookTracker is an ASP.NET Core Blazor Web App for managing a personal book library. It runs in **Interactive Server** render mode (Blazor Server), backed by EF Core + SQL Server. It includes AI-powered features via the Anthropic API (Claude).

Target deployment: Azure App Service + Azure SQL (Basic tier) via GitHub Actions.

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
Book
  Title, Subtitle, Author, Category (Fiction/NonFiction), Status (Unread/Reading/Read),
  Rating (0-5), Notes, DateAdded, DefaultCoverArtUrl
  ├── Editions (1-to-many)
  │     ISBN (unique), Format (Hardcopy/Softcopy), DatePrinted, CoverUrl
  │     ├── Copies (1-to-many)
  │     │     Condition (AsNew..Poor), DateAcquired, Notes
  │     └── Publisher (many-to-1, optional)
  ├── Genres (many-to-many, hierarchical — parent/child)
  ├── Tags (many-to-many, e.g. "follow-up")
  └── Series (many-to-1, optional)
        SeriesOrder (position in series)

Series
  Name (unique), Author (optional), Type (Series/Collection),
  ExpectedCount (for numbered series), Description
  └── Books (1-to-many)

Genre
  Name (unique), ParentGenre (self-referential)
  └── Children (1-to-many)

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
  Static seed data — 48 curated genres in a hierarchical taxonomy
```

Key design decisions:
- **Book vs Edition vs Copy**: A Book is the abstract work. An Edition is a specific printing with a unique ISBN (hardcover vs softcover are different Editions). A Copy is a physical item you own.
- **Edition.ISBN is unique**: scanning an ISBN always resolves to one Edition.
- **Series.Type**: `Series` = numbered with known order; `Collection` = loose grouping.
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

## Services

### BookLookupService
Looks up book metadata by ISBN. Tries Open Library first, falls back to Google Books. Returns `BookLookupResult` with title, author, publisher, genres, cover URL, etc.

### SeriesMatchService
Local series detection after ISBN lookup. Strategies:
1. Author already has a series — suggests it
2. Author has multiple series — matches by title
3. Author has 2+ ungrouped books — suggests creating a collection
4. Title contains series indicators (Book #, Vol., Part II)

### AIAssistantService (IAIAssistantService)
Wraps the Anthropic API. Scoped lifetime for prompt caching within a session.

| Method | Model | Purpose |
|--------|-------|---------|
| `SuggestGenresAsync` | Sonnet | Suggest genres from preset taxonomy |
| `SuggestCollectionsAsync` | Sonnet | Suggest series/collection groupings for uncategorised books |
| `SuggestShoppingListAsync` | Sonnet | Recommend books based on library patterns and series gaps |
| `AssessBookAsync` | Opus | Suitability assessment for a specific book/author |

All prompts use `PromptCacheType.FineGrained` with `CacheControlEphemeral` on system messages.

## Barcode scanning

ISBN barcodes (EAN-13/EAN-8) are scanned via the `html5-qrcode` library (v2.3.8, static JS under `wwwroot/lib/`). A JS interop wrapper (`barcode-scanner.js`) manages the scanner lifecycle. The scanning viewport is optimised for 1D barcodes (wide and short, not square). Used on Bulk Add and Shopping pages.

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
| `ConnectionStrings:DefaultConnection` | appsettings / Azure config | SQL Server connection |
| `Anthropic:ApiKey` | appsettings / Azure config | Anthropic API key |
| `Anthropic:FastModel` | appsettings (default: Sonnet 4.5) | Model for batch AI ops |
| `Anthropic:DeepModel` | appsettings (default: Opus 4.5) | Model for deep analysis |

Dev config templates: `appsettings.Example.json`, `appsettings.Development.Example.json`. Copy and fill in secrets.

## Infrastructure

- Docker Compose for local SQL Server 2022 Developer
- GitHub Actions CI (build + test on PR)
- Dependabot for NuGet (EF Core grouped) and npm (html5-qrcode)
- Azure Bicep templates under `infra/`
- Auto-migration on startup (TODO: deploy-time migration bundle)

## Key conventions

- **Branching**: never commit to main. Feature branches with `feat/`, `fix/`, `chore/`, `docs/`, `refactor/` prefixes. Squash-merge.
- **Planning**: every feature gets a plan first. PRs split by concern for medium+ complexity.
- **Performance target**: system must handle 3,000+ book copies.
- **Deployment safety**: migrations must retain data. Breaking changes need defaults.
- **Mobile priority**: every new feature should clarify if mobile+web or web-only.
