# Architecture

This document describes the overall design and structure of BookTracker. It should be kept up to date as the system evolves.

## Overview

BookTracker is the codebase / namespace umbrella for **two apps**:

- **Bookcase** — the ASP.NET Core Blazor Web App (Interactive Server) backed by EF Core + Azure SQL. Catalog, edit, browse, AI assistant, bookshop-mode scanning. PWA-installable on mobile and desktop. Deployed at `books.silly.ninja`.
- **Bookshelf** — the .NET MAUI Android companion. Offline-capable in-bookshop tool. Consumes a slim JSON catalog snapshot from Bookcase; doesn't talk to SQL directly.

AI features sit in Bookcase via multiple providers: Anthropic (Claude, public API) and Azure OpenAI (GPT-4o, Azure-hosted). Microsoft Foundry (Claude on Azure) is supported in code but not currently provisioned — see `infra/README.md` and `TODO.md`.

Target deployment for Bookcase: Azure App Service + Azure SQL (Basic tier) via GitHub Actions. SQL, Key Vault, and Azure OpenAI sit behind Private Endpoints; the App Service reaches them through VNet integration + a peered eastus2 VNet for the OpenAI account. Bookshelf is an APK that talks to Bookcase's `/api/*` surface over HTTPS with AAD bearer tokens.

## Solution structure

```
BookTracker.slnx
  BookTracker.Data/                Class library — entities (rich aggregates), DbContext, EF migrations
  BookTracker.Application/         Command + query handlers, the IDispatcher, read-model
                                   projections + formatters — the DDD/CQRS spine (TODO #55)
  BookTracker.Shared/              Wire-format DTOs (CatalogSnapshot, BookSnapshot, …)
                                   — no EF dependency, referenced by Web + Mobile
  BookTracker.Web/                 Blazor Web App (Bookcase) — UI, ViewModels (dispatch-only), /api/*
  BookTracker.Mobile/              .NET MAUI Android app (Bookshelf) — net10.0-android
  BookTracker.Mobile.Cache/        SQLite-backed catalog cache library — pure net10.0
                                   (referenced by Mobile + tested independently)
  BookTracker.Mobile.Cache.Tests/  xUnit tests for the cache, no MAUI runtime needed
  BookTracker.Tests/               xUnit tests for Web — real SQL via Testcontainers
```

All projects target `net10.0` (the Mobile project adds `net10.0-android`).

## Data model

```
Book                                                ← physical-object grouping
  Title, Category (Fiction/NonFiction),
  Status (Unread/Reading/Read/Reference), Rating (0-5),
  Notes, DateAdded, DefaultCoverArtUrl,
  UpdatedAt (DATETIME2, default GETUTCDATE(), indexed),
  DeletedAt (nullable, filtered index, soft-delete tombstone)
  ├── Series (many-to-1, optional) + SeriesOrder (int sort key)
  │     + SeriesOrderDisplay (optional label, e.g. "4.5" interquel)
  │           ← the book is installment N of a publication series
  ├── Works (many-to-many via BookWork join)         ← what's actually inside the book
  │     BookWork.Order = per-book display position (0-based, appends on add,
  │       user-reorderable); works render in this order, not alphabetically
  │     Title, Subtitle, FirstPublishedDate
  │     ├── Author (many-to-1 → Author)
  │     └── Genres (many-to-many → Genre)
  ├── Editions (1-to-many)
  │     ISBN (filtered unique, nullable for pre-ISBN books),
  │     Format, EditionNumber (nullable — "3rd ed."),
  │     DatePrinted, CoverUrl
  │     ├── Copies (1-to-many)
  │     │     Condition (AsNew..Poor), DateAcquired, Notes
  │     └── Publisher (many-to-1, optional)
  └── Tags (many-to-many, e.g. "follow-up")

Series
  Name (unique), Author (string, optional, display-only),
  Type (Series/Collection),
  ExpectedCount (for numbered series), Description
  └── Books (1-to-many)

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
- **Work vs Book vs Edition vs Copy**: A `Work` is the abstract creative unit (a story / novel / play / poem). A `Book` is the physical-object grouping containing one or more Works (a compendium contains many; a novel contains one). An `Edition` is a specific printing of the Book with a unique ISBN. A `Copy` is the physical item you own. Authorship, subtitle, and genres belong to the Work, not the Book — a Christie short-story collection is "horror" because each contained story is, not because the volume itself is tagged. Series membership belongs to the **Book** — the book is installment N of a publication series (see Series.Type below).
- **Author entity with self-referential pen names**: each `Work` points at a specific `Author` row. Pen names (Richard Bachman) get their own Author row with `CanonicalAuthorId` pointing at the canonical (Stephen King). A Bachman novel is *displayed* as "by Richard Bachman" (the Work's `AuthorId` points at Bachman) but aggregations group by `CanonicalAuthorId ?? Id` so King's tally includes the Bachman titles. Lookups are find-or-created **eagerly at the picker** (typing a fresh name and committing it — chip Enter/comma, dropdown-pick, or field blur — creates the row immediately; TD-15a), with find-or-create on save kept as a net for ISBN-prefilled names that bypass the picker. Merging duplicates and managing aliases happens on `/authors`.
- **Single-Work books are the common case** — the Add page auto-creates one Work alongside the Book and mirrors the title between them. Compendium support (multiple Works per Book) lives on the Edit page's "Other works" section.
- **Edition.ISBN is unique** (filtered — pre-1974 books with no ISBN coexist as nullable rows).
- **Series.Type**: `Series` = numbered with known order; `Collection` = loose grouping. Series membership is **per-Book** — the book is installment N of a publication series. A single-Work novel and a multi-Work short-story collection both sit in the series as one installment; the constituent Works carry no series of their own.
- **Genre hierarchy**: top-level genres with sub-genres. Selecting a sub-genre auto-selects its parent.
- **Loans live on the Copy**: a `Loan` (free-text `BorrowerName`, `LentOn`, optional `DueOn`, `ReturnedOn` null while open) belongs to the Book aggregate through `Copy.Loans`; `Copy.LendTo` / `Copy.MarkReturned` are the only write paths, a copy has at most one open loan (entity check plus a filtered unique index on `CopyId WHERE ReturnedOn IS NULL`), returned loans stay as history, and `Book.SoftDelete()` refuses while any copy is lent out.
- **Book.UpdatedAt** is auto-stamped on aggregate change via `BookUpdatedAtInterceptor` (a `SaveChangesInterceptor`) — any change to the Book itself OR to its Edition / Copy / Loan / Work / WorkAuthor / Tag membership bumps the Book row's timestamp. Backs the delta-sync `?since=` filter on `/api/catalog-snapshot`. Save sites don't need to set it explicitly. A value converter pins `Kind=Utc` on read so cross-timezone clients round-trip the watermark correctly.
- **Book.DeletedAt** drives a soft-delete shape: a global EF `HasQueryFilter(b => b.DeletedAt == null)` hides tombstoned rows from every normal query (Library / View / search / merge). The husk row survives only so the catalog snapshot can emit it in `deletedIds[]` for Bookshelf clients to drop locally. Aggregate children (Editions + Copies + joins) are hard-removed at delete time — the husk has no aggregate. `Book.SoftDelete()` is the one write path; `DeleteBookHandler` and `MergeBooksHandler` (loser-side) are its two callers.

## Architecture pattern — MVVM over a CQRS spine

The app follows MVVM at the UI edge, with all data access behind a thin DDD/CQRS
application layer (the [back-end refactor arc](#application-layer--cqrs-spine),
TODO #55):

```
[Razor Component (.razor)]  ←→  [ViewModel (.cs)]  ──IDispatcher──▶  [Command / Query handlers]
     View (UI binding)           State + UI logic                    (BookTracker.Application)
                                  (no DbContext)                              │
                                                                    ┌─────────┴─────────┐
                                                              [Rich aggregates]   [Read projections]
                                                              (BookTracker.Data)   (AsNoTracking → DTO)
```

- **ViewModels** are plain C# classes under `BookTracker.Web/ViewModels/`. They hold UI state + presentation logic and **dispatch commands/queries** — they never touch `DbContext`; every read and write goes through `IDispatcher`.
- **Razor components** are thin views that inject a VM and bind to its properties. UI concerns only: JS interop, navigation, Blazor lifecycle.
- **Services** handle only external concerns: ISBN lookup (Open Library / Google Books / Trove), the LLM calls behind the AI features, and cover storage. They hold no library reads: a service that needs library facts dispatches a query through `IDispatcher`, as a ViewModel does. Merges are commands (see below).

VM lifetime:
- **Transient**: most VMs — each component instance gets its own.
- **Scoped**: `AIAssistantViewModel` and `IAIAssistantService` — shared across the Blazor circuit so the call counter and prompt cache persist per session.

## Application layer — CQRS spine

`BookTracker.Application` is the seam between the UI and the data. It owns the
write **and** read logic that used to live inline in ViewModels and services.
Design + binding conventions (C1–C12) live in `docs/BACKEND-REFACTOR-DESIGN.md`.
`BookTracker.Tests/Architecture/WebLayeringTests.cs` enforces the seam: no type in `BookTracker.Web` takes or holds a `BookTrackerDbContext`, its factory or a `DbSet<>` except the five exceptions (`ProgramSetup`, `WarmupEndpoints`, `CoverMirrorBackgroundService`, `CoverMirrorQueue`, `EditionFormatBackfillService`), and no ViewModel or component holds a Data entity (Data enums are allowed).

- **Dispatcher.** A thin hand-rolled `IDispatcher` (`Send(ICommand)` / `Query(IQuery<T>)`) — **no MediatR**. Handlers are discovered by a convention scan (`AddApplicationLayer()`) over the assembly, so a new handler is registered the moment it exists (which is why the integration tests auto-track relocated reads).
- **Commands** name **atomic user gestures, not table columns** (C10): `MarkBookRead(rating, notes)` is one command, not `SetStatus` + `RateBook` + `UpdateNotes` — one load, one save, one `UpdatedAt` bump. Handlers load the aggregate, call a method on it, and save; invariants live **on the entity** (e.g. `Book.Rate` enforces 0–5, `Work` self-orphans at ref-count 0 — C11). `Book`'s scalar fields are written only through its methods — the setters are private (`UpdatedAt` / `DeletedAt` internal, for the interceptor and `SoftDelete`) and the compiler enforces it; its collections (`Editions`, `Tags`, `Works`, `BookWorks`) keep public setters until the collections slice encapsulates them. Merge operations are transactional commands (`MergeBooks`/`MergeWorks`/`MergeAuthors`/`MergeEditions`/`MergePublishers`).
- **Queries** are read-only `AsNoTracking()` SQL **projections to custom read-model records** (C5), not `Include` graphs — the heavy lifting stays in SQL and only the displayed scalars cross the wire (see [[feedback_light_reads_custom_projections]]). Each display VM has a matching `Get*` query handler under `Application/<Feature>/`.
- **Shared read-models (close-out consolidation).** Counts/cover-picks that several readers need have one owner: `AuthorRollups` (per-author distinct work/book/series counts via correlated `COUNT(DISTINCT)` subqueries; canonical totals sum the alias members) backs `/authors`, the catalog snapshot, and Home's top-authors; `GenreReads.TopGenresAsync` backs Home + the AI prompt; `BookCovers.PickAsync` is the single-Work-cover-else-any pick shared by the merge previews. Pure display formatters (`WorkAuthorshipFormatter`, `SeriesOrderParser`, `PartialDateParser`, `BookFormatExtensions`) live in `Application/Formatting/` so a read handler can project a formatted field on the same side of the boundary as its DTO.
- **Testing shape.** DB-backed assertions are integration tests under `BookTracker.Tests/Application/<Feature>/*HandlerTests` (real SQL via Testcontainers, dispatched through `AddApplicationLayer()`); VMs keep thin `IDispatcher`-mock unit tests over the presentation logic that genuinely stayed in the VM.

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
| `/books` | Library | Filterable book list (search, category, genre, tag, author, **status**, **series**). Group-by picker (Author / Genre / None); a grouped view renders each group as a row that drills into a flat, filtered book list. **Series browsing is the Series filter + reading-order sort, not a grouping mode** (the grouped Series view was retired — TODO #53c). Filters reduce within groups. Desktop table + mobile cards. **Inline status/rating quick-set:** each row's status chip is a menu and its stars are editable (without opening the book) — picking **Read** opens `MarkReadDialog` to capture rating + notes in one step; other statuses persist immediately. Edited rows are patched in place (not re-queried) so a book moved out of the active status filter stays visible until the next explicit reload. The status filter doubles as a re-triage worklist. A book with a copy out on loan shows a "Lent to {borrower}" chip on its row and card (`BookListItem.LentTo`); it is a marker only and filters nothing. |
| `/books/add` | Add Book | ISBN lookup + manual entry. Creates Book + Edition + Copy. Series suggestion after lookup **plus a manual series typeahead** (pick existing or create new + order). Collection mode for multi-Work compendiums with inline existing-Work attach via title typeahead. |
| `/books/{id}` | Book Detail | Default browsing surface for a single book. Read-only scaffold with inline auto-save (rating / status / notes / tags) plus a Delete affordance. Modal edits open `BookEditDialog` / `WorkEditDialog` / `EditionFormDialog` / `CopyFormDialog` / `AddWorkDialog`. Each copy can be lent (`LendDialog`, which lends several copies to one borrower in one go) and marked returned from here. The `/books/{id}/edit` "full edit page" escape hatch was decommissioned — every edit surface is now reachable from here. |
| `/books/bulk-add` | Bulk Add | Rapid ISBN entry (text or barcode scanner). Discovery grid with async lookup, accept/follow-up, duplicate detection. An opt-in "Ask AI for genres" switch (off by default, per circuit) asks the active provider for genres during a row's lookup when the rule-based match over the lookup's subject strings finds nothing or only top-level genres; the row lists its genres before Accept with AI-sourced names marked "AI". |
| `/bookshop` | Bookshop Mode (PWA) | Mobile-optimised offline-capable surface for the "in a real bookshop" use case. ISBN scan + manual ISBN + author lookup tabs, backed by an IndexedDB cache populated from `/api/catalog-snapshot`. Sibling of Bookshelf MAUI — separate cache, same DTO shape. |
| `/series` | Series List | All series/collections with completion status |
| `/series/new` | New Series | Create a new series or collection |
| `/series/{id}` | Edit Series | Edit series, manage books in series, reorder |
| `/shelf-order` | Shelf order | Read-only list for shelving at the bookcase, readable on a phone and printable: books grouped by the top-level genre of their first Work (the alphabetically first root when there are several, others shown as "also"; no genre → a last "Unsorted" group), each group ordered by lead-author surname, series and series order, year first published, then title (`GetShelfOrder`, `ShelfOrderKey` in `Application/Shelf/`). |
| `/loans` | On loan | Copies currently lent out (`GetOpenLoans`), grouped by borrower with a count per borrower; each row shows cover, title (links to the book), author, lend date, due date or "no return date", and an "Overdue" chip from the day after the due date (`LoanDates`, owner's time zone). "Returned" closes the loan as of today (`ReturnLoan`) and drops the row in place. A collapsed History panel loads the returned loans on first expand (`GetLoanHistory`, newest return first, at most 200). Card rows at every width, so it reads on a phone. |
| `/shopping` | Shopping Mode | Earlier mobile-optimised page covering scan + series gaps + wishlist. Overlaps with `/bookshop`; TODO #28 tracks the consolidation. |
| `/assistant` | AI Assistant | Book advisor (Opus), genre cleanup, collection cataloguing, shopping suggestions (Sonnet). |
| `/authors` | Authors | MudBlazor list with per-row drill-down to Works/Books, alias rollup on canonical rows, inline rename / merge / alias-resolve. Deep-linked from Home top-10. |
| `/authors/{id}` | Author Detail | Per-author drill-down — works list, alias graph, attached books. |
| `/publishers` | Publishers | MudBlazor list mirroring `/authors` structurally — per-row drill-down to editions, inline rename, two-step-confirm merge (no alias model — outright absorption), delete-unused. |
| `/duplicates` | Duplicates | Tabs for Authors / Works / Books / Editions. Lists candidate duplicate pairs detected on-demand. Dismiss false positives (reversible via the "Dismissed" section). Author pairs have a Merge → button. Web-primary, desktop-first layout. |
| `/duplicates/merge/author/{idA}/{idB}` | Merge authors | Side-by-side review of the pair, radio to pick a winner, preview of impact (N works + M aliases to reassign), transactional merge. Refuses when the two authors resolve to different canonicals — user resolves aliases on `/authors` first. |
| `/duplicates/merge/work/{idA}/{idB}` | Merge works | Side-by-side review, radio to pick a winner, preview of impact (books to reassign + any books that already contain both → loser dropped). Transactional with auto-fill-empties semantics. Refuses if the two resolve to different authors (merge authors first). |
| `/duplicates/merge/edition/{idA}/{idB}` | Merge editions | Side-by-side review with cover thumbnails, winner radio, preview of copies to reassign + which empty winner fields will be auto-filled from loser (ISBN, date printed, publisher, cover). Refuses cross-book edition merges (merge the Books first). |
| `/duplicates/merge/book/{idA}/{idB}` | Merge books | Side-by-side review with cover thumbnail, winner radio, preview of editions to reassign + works / tags to union + auto-fill hints (notes, cover, rating-if-unrated). No structural incompatibility path — Book merge is the aggregator and everything beneath it is moved or unioned. |

## API endpoints (Minimal API)

Lightweight JSON surface consumed by Bookshelf (MAUI) and `/bookshop` (PWA). Each domain gets its own `*Endpoints.cs` static class with an extension method on `IEndpointRouteBuilder`; `ProgramSetup.cs` maps them all together. All routes (except `/warmup`) are gated by Easy Auth at the App Service platform layer — there's no app-side auth middleware.

| Route | Purpose |
|-------|---------|
| `GET /api/catalog-snapshot[?since=<ISO 8601 UTC>]` | Slim catalog JSON. Full snapshot when `since` is absent; delta of changed Books + `deletedIds[]` tombstones when present. Returns Authors + Series in full regardless of `since` (they're tiny + the rename-propagation case is brittle on a filtered list). The response carries a `LatestUpdatedAt` watermark the client stores and sends back as the next `?since=`. Drives Bookshelf catalog refresh + `/bookshop` cache hydration. |
| `GET /api/wishlist-snapshot` | Read-only wishlist projection for the `/bookshop` "shopping list" tab. Bookcase web remains the canonical writer (add/remove/edit). |
| `GET /api/online/isbn/{isbn}` | Resolves an ISBN the phone does not own against the provider chain (Open Library, then Google Books, then Trove; keys stay server-side) and returns an `OnlineIsbnLookup`. `Owned` names the library Book holding that exact edition (`SameEdition = true`) or, failing that, a live Book holding the same Work by normalised title and lead-author surname (`SameEdition = false`); null when the library owns neither. `WorkKey` is Open Library's work key from its `jscmd=details` record (null when another provider answered), for the phone to pass to `work-editions`. `400` when the cleaned ISBN is not 10 or 13 characters, `404` when no provider knows it. Consumed by Bookshelf's online layer. |
| `GET /api/online/author-works?author=<name>` | An author's bibliography from Open Library (the only provider for this call; up to 100 works, most editions first) as `OnlineAuthorWorks`. Each `OnlineAuthorWork`'s `OwnedBookId` is the lowest-Id live Book holding a Work of the same normalised title whose lead author has the same normalised name, pen names rolled up both ways through `CanonicalAuthorId`; null when not owned. `400` when the name is blank; a provider failure is an empty `Works`. Consumed by Bookshelf's online layer ("Missing only"). |
| `GET /api/online/work-editions?workKey=<key>` | The editions of an Open Library work (the only provider for this call; up to 50) as `OnlineWorkEditions`. Each `OnlineEdition` carries the first ISBN-13 else ISBN-10 (cleaned), title, publisher, Open Library's publish date and physical format as given, and a cover URL (by cover id, else by ISBN); an edition without an ISBN is listed only when it has both a title and a publisher. `OwnedBookId` is the live Book holding an Edition with that exact ISBN (`FindOwnedEditions`); null when not owned. `400` when the key is not of the form `/works/OL…W`; a provider failure is an empty `Editions`. Consumed by Bookshelf's online layer (other editions of a work). |
| `GET /warmup` | Anonymous slot-swap warmup probe — wired via `WEBSITE_SWAP_WARMUP_PING_PATH` in Bicep. Does a trivial `Books.Take(1)` to warm the SQL connection pool + managed-identity AAD token before the slot promotes. One of the two paths excluded from Easy Auth (alongside PWA static assets). |

## Shared components

| Component | Purpose |
|-----------|---------|
| `BookForm.razor` | Book metadata fields (title, author, category, status, rating, notes, cover URL) + cover preview |
| `GenrePicker.razor` | Hierarchical genre checkbox grid with fuzzy matching from ISBN lookup |
| `EditionCopyForm.razor` | Edition fields (ISBN, format, publisher, date, cover) + copy condition |
| `AIProviderToggle.razor` | Dropdown to switch AI provider at runtime + call counter badge |

## Services

> **Note (back-end refactor arc, TODO #55):** the catalog/wishlist snapshot
> *services* and the four per-entity *merge services* listed below were dissolved
> into `BookTracker.Application` during the arc — their read logic became `Get*`
> query handlers and their writes became transactional `Merge*` commands. The
> entries are kept here because they still describe the **domain behaviour**
> faithfully (the projection shapes and merge semantics are unchanged); only the
> home of the code moved. New handler locations are noted inline.

### GetCatalogSnapshot (was CatalogSnapshotService)
*Now `BookTracker.Application/Catalog/GetCatalogSnapshot.cs` (query handler).*
Projects the catalog into the slim wire-format `CatalogSnapshot` consumed by Bookshelf (MAUI) and `/bookshop` (PWA). Carries Books (with nested Editions + Works for the enhanced ScanPage view — each `WorkSnapshot` carries its per-book `Order` so an anthology's works render in the same capture / user-reordered order as the web; the mobile cache sorts `Order` then `WorkId` so legacy rows without an order fall back to the old ordering), Authors (with canonical/alias rollup counts), Series (always full-listed regardless of `since`), and the `LatestUpdatedAt` watermark + `DeletedIds[]` tombstones for delta-sync. The `?since=` filter is a B-tree seek on `IX_Books_UpdatedAt`; tombstones come from `IgnoreQueryFilters().Where(DeletedAt > since)` since the soft-delete filter would otherwise hide them.

### GetWishlistSnapshot (was WishlistSnapshotService)
*Now `BookTracker.Application/Catalog/GetWishlistSnapshot.cs` (query handler).*
Read-only wishlist projection for `/api/wishlist-snapshot`. Bookcase web remains the canonical write surface — add/remove/edit happen there. `/bookshop` and the future Bookshelf wishlist surface consume this DTO read-only and synthesise a local "bought" flag that self-heals on each catalog refresh.

### BookCoverStorage
Mirrors upstream cover URLs (Open Library / Google Books / Trove) into Azure Blob Storage so renders never depend on upstream latency. Fetches only `https` URLs with a DNS host name on the default port, refuses hosts that resolve to loopback, private, link-local, shared-address-space or multicast addresses (checked in the connect callback, so the connection goes to the checked address), and follows at most three redirects, re-checking each hop (`CoverFetchPolicy`); a refused URL is skipped with a logged warning (user info stripped from the logged URL) and the row keeps its upstream URL. The same URL rule (`CoverFetchPolicy.IsAcceptableCoverUrl`) applies when a form saves a cover URL (the `[CoverUrl]` attribute on the Add page inputs and a field `Validation` plus a Save guard on the book and edition dialogs, which apply it only to a changed URL so a stored one stays saveable) and when an ISBN lookup returns one (the first provider cover URL that passes it wins; none passing leaves the cover empty). Downloads only responses whose `Content-Type` is an allowed raster type (`image/jpeg`, `image/png`, `image/gif`, `image/webp`) and at most 10 MiB (checked against `Content-Length` and again while reading the stream); anything else is skipped with a logged warning and the row keeps its upstream URL. Normalises via SkiaSharp (JPEG, max 1200px long edge; an allowed type that SkiaSharp cannot decode is skipped with a logged warning and the row keeps its upstream URL; non-raster types such as SVG are never stored), uploads to the `book-covers` container, swaps the URL on `Edition.CoverUrl` / `Book.DefaultCoverArtUrl` to the blob URL.

`CoverMirrorBackgroundService` (hosted service) polls every 30s for un-mirrored URLs and processes them in batches of 50. Handles both the initial backfill and ongoing mirroring. `MirrorFromUrlAsync` classifies each attempt as mirrored, already managed, a permanent refusal (refused URL or redirect, too many redirects, a 4xx other than 408 and 429, a disallowed content type, over the size cap, undecodable) or a transient failure (408, 429, 5xx, timeout, network or blob-upload error). Each unsuccessful attempt is recorded in the `CoverMirrorState` table (one row per Edition or Book cover, keyed by target kind and id, holding the URL, attempt count, last and next attempt times and last outcome; not part of the Book aggregate, so it never bumps `Book.UpdatedAt`). A transient failure is retried on a backoff that starts at 1 minute and doubles per attempt up to 24 hours (`CoverMirrorBackoff`); a permanent refusal is not retried while the row keeps that URL. A changed URL makes the row due again at once with the attempt count restarted, a successful mirror deletes the state row, and every tick deletes state rows whose Edition or Book no longer exists (a soft-deleted Book counts as gone). The due-row queries and state writes live in `CoverMirrorQueue`. Local dev uses Azurite on `localhost:10000`; prod uses a real Storage Account provisioned by `infra/modules/cover-storage.bicep`. `CoverBlobServiceClientFactory` builds the blob client from `CoverStorage:ConnectionString` (account key, held in Key Vault in Azure) when it is set, and otherwise from `CoverStorage:ServiceUri` (the account's blob endpoint) with `DefaultAzureCredential`, which in App Service is the slot's managed identity (Storage Blob Data Contributor on its own container); cover storage is enabled when either is set. The container is created with blob-level public read on first use if it is missing.

### BookUpdatedAtInterceptor
`SaveChangesInterceptor` registered on the DbContextFactory. Walks `ChangeTracker.Entries()` on every save; bumps `Book.UpdatedAt` whenever any aggregate entity (Book itself, Edition, Copy, Loan, Work, WorkAuthor, BookTag join, Tag rename) changes. Special-cases skip-nav collection changes on Book (`book.Tags.Add(tag)` leaves Book.State = Unchanged but the Collection is IsModified). Single source of truth — save sites don't need to remember the discipline.

### BookLookupService
Looks up book metadata by ISBN. Tries Open Library first, falls back to Google Books, then Trove (NLA) as a coverage-of-last-resort for self-published / Australian titles the other two tend to miss. Trove is skipped silently when no API key is configured. Returns `BookLookupResult` with title, author, publisher, genres, cover URL, etc.

### Duplicate detection
*The `GetDuplicateReport` query and the `SweepOrphanedDismissals`, `DismissDuplicatePair` and `RestoreDismissedDuplicate` commands in `BookTracker.Application/Duplicates/`.*
`GetDuplicateReport` scans the library for candidate duplicate pairs across Authors, Works, Books, and Editions and returns a `DuplicateReport`; it is read-only (every read `AsNoTracking()`). Authors match on either normalised full name *or* shared surname + first-name initial (so "Doug Preston" / "Douglas Preston" / "D Preston" all surface together). Works, Books, and Editions use exact-after-normalisation (`DuplicateNormalization`). Dismissed pairs are persisted in `IgnoredDuplicate` (polymorphic table, unique on `(EntityType, LowerId, HigherId)`) and come back on their pair as `Dismissed`. `DismissDuplicatePair` stores a pair lower-id-first and is a no-op for equal ids or an existing dismissal; `RestoreDismissedDuplicate` deletes a dismissal and is a no-op when it is gone. `SweepOrphanedDismissals` deletes dismissals whose ids no longer name an existing row; `DuplicatesViewModel` dispatches it before `GetDuplicateReport` on every load of `/duplicates`.

### MergeAuthors (was AuthorMergeService)
*Now the `MergeAuthors` command + `GetAuthorMergePreview` query in `BookTracker.Application/Authors/`; compatibility check shared via `AuthorMergeCompatibility`.*
Merges two Author rows after user review. Refuses when the two authors resolve to different canonicals (user must resolve aliases on `/authors` first). Otherwise runs in one transaction: reassigns `Work.AuthorId`, reassigns external aliases' `CanonicalAuthorId`, clears any `IgnoredDuplicate` rows mentioning the loser, deletes the loser. One edge case: when the winner is itself an alias of the loser, winner is promoted to canonical before the delete so its `CanonicalAuthorId` doesn't dangle. Returns a result with reassignment counts + a flag for the promotion case.

### MergeWorks (was WorkMergeService)
*Now the `MergeWorks` command + `GetWorkMergePreview` query in `BookTracker.Application/Works/`.*
Merges two Work rows after user review. **Auto-fill-empties** semantics: any winner field that's null/empty gets taken from loser (Subtitle, FirstPublishedDate+Precision pair, SeriesId+SeriesOrder+SeriesOrderDisplay pair); Genres are unioned. Fields the winner already has are preserved. The VM's `EnrichmentHints` surfaces exactly what will move so the user can override (by editing the winner) before confirming. Refuses if the two Works have different `AuthorId`. Transactional: for each Book attached to the loser, adds the winner if not already present then clears `loser.Books`, which deletes the `BookWork` rows; clears any `IgnoredDuplicate` referencing the loser; deletes the loser Work. The "Book contains both" count is surfaced in preview + result so the UI can flag that those Books will just lose the loser attachment (winner stays).

### MergeEditions (was EditionMergeService)
*Now the `MergeEditions` command + `GetEditionMergePreview` query in `BookTracker.Application/Books/`.*
Merges two Edition rows belonging to the same Book after user review. Same shape as WorkMergeService: transactional, auto-fill-empties (ISBN, DatePrinted+Precision pair, CoverUrl, PublisherId), reassigns `Copy.EditionId`, clears stale `IgnoredDuplicate` rows, deletes the loser Edition. Refuses cross-Book merges (if the Editions are really the same, the Books themselves are duplicates and the Book-level merge should happen first).

### MergeBooks (was BookMergeService)
*Now the `MergeBooks` command + `GetBookMergePreview` query in `BookTracker.Application/Books/`; loser-side soft-delete.*
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

**Library facts:** the providers take `IDispatcher` and read the library through the queries in `Application/Ai/`: `GetGenreTaxonomy` (genre suggestions), `GetUnseriesedBooksForAi` plus `GetSeriesList` (collection suggestions), `GetAiLibraryProfile` (shopping list: top authors, top genres, highly rated books, and the series gaps from `GetSeriesGaps`, the same rule as the `/wishlist` series-gaps section) and `GetLibraryProfileForAdvisor` (book advisor).

**Shared logic:** `AiPromptContext` renders those read models into the prompt text, and `SharedParsers` holds the prompt templates and the JSON parsing; all three providers use both.

**Usage and cost:** after every successful call each provider dispatches `RecordAiUsage` (`Application/Ai/`), which writes one `AiUsage` row: provider name, model (the model id for Anthropic, the deployment name for Azure OpenAI and Microsoft Foundry), operation (`SuggestGenres`, `SuggestCollections`, `SuggestShoppingList`, `AssessBook`, `ExtractIsbn`) and the input and output token counts the provider reported, stamped with the UTC time. A response with no usage block records nothing (one warning per service instance), and a failure while recording is logged and never fails the AI call. `GetAiUsageSummary` groups the rows in SQL by provider and model, once for the current UTC month and once for all time, and prices each line from `AI:Prices` (US dollars per million input and output tokens, keyed by model string, case-insensitive; `AiUsageCost.Compute`); a model with no price shows its tokens with a null cost. `/assistant` shows one line of this month's totals across providers ("This month: 12 calls, 84,210 in / 6,120 out tokens, about $0.35", plus a count of models without a price), loaded with the page and refreshed after each AI action; the per-circuit `CallCount` chip in `AIProviderToggle` is unchanged. `AiUsage` is bookkeeping outside every aggregate, so a usage row never bumps `Book.UpdatedAt`.

| Method | Speed | Purpose |
|--------|-------|---------|
| `ExtractIsbnFromImageAsync` | Fast | OCR ISBN from photo (vision API) |
| `SuggestGenresAsync` | Fast | Suggest genres from preset taxonomy (the AI Assistant's genre pass; Bulk Add behind its "Ask AI for genres" switch) |
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

- **Pages using MudBlazor:** Home, Duplicates/MergeBook, Book Detail (`/books/{id}`) and its dialogs (BookEditDialog, WorkEditDialog, EditionFormDialog, CopyFormDialog, AddWorkDialog), the `MudGenrePicker` + `MudAuthorPicker` + `MudContributorPicker` shared components, Authors, Publishers, Book Add, Book Bulk Add. These use `MudCard`, `MudButton`, `MudText`, `MudContainer`, `MudDialog`, `MudAutocomplete`, etc. `/books/{id}/edit` was decommissioned — every edit surface now reachable from Book Detail.
  - `MudAuthorPicker` handles `Role=Author` contributors (the dominant case). `MudContributorPicker` is its sibling for non-Author roles (editor / translator / illustrator / etc.) — used on every Work-editing surface (Add Book single-Work + per-collection-row, AddWorkDialog, WorkEditDialog). The two-picker split keeps the common single-Author case zero-cost while making editor / translator / illustrator credit explicit when needed; see `AuthorRole` in `BookTracker.Data/Models/WorkAuthor.cs` for the role list.
- **Pages still on Bootstrap:** Library list, Series, Shopping, AI Assistant, Duplicates list. The navbar in `MainLayout.razor` is still Bootstrap.
- **Rollout strategy:** no migration deadline — pages convert as they're touched for other reasons. Low-traffic pages may stay Bootstrap indefinitely; that's fine.
- **Coexistence:** both stylesheets are loaded in `App.razor`. MudBlazor's four root providers (`MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`) sit in `MainLayout.razor` — harmless on Bootstrap-only pages. Each page picks one lane.
- **Theme:** custom "warm library" palette in `BookTracker.Web/Theme/BookTrackerTheme.cs` (oxblood / antique brass / forest / parchment / espresso). Applied globally via `<MudThemeProvider Theme="BookTrackerTheme.Default" />`. Dark mode not wired yet.

## Mobile responsiveness (Bookcase web)

Key mobile workflows: barcode scanning, library search, shopping mode. Pages use Bootstrap responsive utilities:
- **Collapsible filters** on Library list (below `md` breakpoint)
- **Card layouts** replacing tables on mobile (Library list, Bulk Add discovery grid, Series list)
- **Scanner-first** on mobile (full-width primary button above text input)

## Bookshelf — MAUI Android companion

Native Android app for the "phone in hand in a bookshop" use case. Read-only against the Bookcase catalog; offline-capable so signal-free bookshops still work.

**Project layout:**
- `BookTracker.Mobile/` (net10.0-android) — MAUI host. **Shell + bottom TabBar** (redesigned 2026-06; see `docs/BOOKSHELF-UI-REDESIGN.md`). Three tabs: **Find** (`FindPage` — debounced owned-library search of authors + works with an All/Authors/Works scope segment, ISBN auto-detect → result, and an inline ZXing scanner), **Wishlist** (`WishlistPage` — Priority/Author/Series sort with High/Medium/Low group headers), **Gaps** (`SeriesGapsPage` — per-series progress bar + missing-volume pills). Pushed pages: `ResultPage` ("do I own this?" answer for a scanned/typed ISBN; when the cache misses and the phone is online it asks `GET /api/online/isbn/{isbn}` and answers at work level) and `AuthorWorksPage` (owned works by an author). `StatusSheetPage` is a modal (sign-in + sync state + cache stats), opened from the ambient `SyncChip` on Find. `Services/SyncService` owns auth + catalog-sync state, shared by the chip, the sheet, and auto-sync-on-launch (`AppShell`). `Theming/` holds the token bridge (`ThemeColors` — `SetThemeColor` → `SetAppThemeColor`), `TablerIcons`, shared cover loading (`Covers`), and the reduced-motion-aware entrance layer (`Motion`). Auth via MSAL public-client OIDC against the same Entra app as Bookcase's Easy Auth. The old hub-and-spoke pages (`MainPage`/`ScanPage`/`AuthorSearchPage`/`AuthorBooksPage`/`TitleSearchPage`) were replaced by this structure.
- `BookTracker.Mobile.Cache/` (pure net10.0) — sqlite-net-pcl-backed `CatalogCache`. Implements `ICatalogCache.PopulateAsync` (full reset) and `ApplyDeltaAsync` (delta merge preserving cover paths when CoverUrl is unchanged). Tables: books, book_isbns, book_editions, book_works, authors, series, meta. Init-time backfill UPDATE handles schema-evolution gaps (sqlite-net-pcl ALTERs but doesn't populate new columns on existing rows). Also hosts `Presentation/`: the Bookshelf pages' pure presentation helpers (ISBN input, book meta line, edition format names, gap pill captions, wishlist sorts, sync-age label, online ISBN answer), kept here rather than in the `net10.0-android` pages so `BookTracker.Mobile.Cache.Tests` covers them.

**Catalog refresh flow:**
1. First load → no stored watermark → full `PopulateAsync`.
2. Subsequent loads → stored watermark sent as `?since=<ISO 8601 UTC>` → `ApplyDeltaAsync` (Books upsert, ISBN/Edition/Work join rows wipe-and-rewrite per Book, Authors+Series fully rewritten, DeletedIds tombstones drop rows).
3. Server `Version` mismatch (deploy changed projection shape) → fall back to full `PopulateAsync` regardless of watermark.

**Cover thumbnails** download on first display via `EnsureCoverCachedAsync`, resized to 200px long-edge JPEG via SkiaSharp, stored at `<db-dir>/covers/{bookId}.jpg`. Survives `ApplyDeltaAsync` when CoverUrl is unchanged (the delta sync's main bandwidth win — no re-downloads on refresh).

**Sharing the DTO contract:** `BookTracker.Shared.Catalog` holds the `CatalogSnapshot` / `BookSnapshot` / `EditionSnapshot` / `WorkSnapshot` / `AuthorSnapshot` / `SeriesSnapshot` records. Web projects them server-side; Mobile deserialises the same wire shape. The PWA `/bookshop` page's IndexedDB cache uses the same DTOs via the JSON response.

**Visual differentiation:** Bookshelf's launcher icon uses the in-app palette (espresso `#3E2723` background, brass `#A67B3A` spine, parchment text lines) — distinct from Bookcase PWA's Material-You purple. See `docs/STYLE-GUIDE.md` §Known inconsistencies for the Bookcase-side resolution still pending.

## Testing

xUnit + NSubstitute. Tests cover ViewModels and services — business logic that can break silently. Pure markup/CSS changes don't need tests.

**Real SQL Server, not InMemory.** Tests run against an MSSQL Testcontainer (one container per process; `SqlServerContainer` is the singleton). `TestDbContextFactory` wraps each instantiation with a Respawn-based wipe + reseed (~50-150ms) so each `new TestDbContextFactory()` is a clean state. The pivot off EF InMemory caught real bugs that InMemory's lax LINQ-to-SQL translation had let through — `?since=` translation, query-filter semantics with `Include`, value converter behaviour on read.

The mobile cache tests (`BookTracker.Mobile.Cache.Tests`) run against real SQLite files (one temp-file per test, GUID-named) — same rationale: in-memory shortcuts hid the schema-evolution gotcha (sqlite-net-pcl's `ALTER ADD COLUMN` doesn't backfill existing rows).

CI runs `dotnet test` on all PRs to main. Playwright E2E lives in `BookTracker.Tests/E2E/` but requires `ms-playwright` browsers installed locally to run outside CI; it's gated by a `Category=E2E` xUnit trait.

## Audit skills

A family of read-only Claude Code skills that flag patterns of interest in specific domains (`security-audit`, `scale-audit`, with `a11y-audit` + `codehealth-audit` planned). Each one is **static-pattern analysis, not runtime measurement** — for runtime metrics see the App Insights queries in [the May 8 perf blog post](blog/2026-05-08-01-i-blamed-the-cold-start-the-trace-disagreed.md). The audit catches "this pattern *might* bite at scale"; App Insights catches "this pattern *is* biting now." Full doc + skill list + pilot history: [`docs/audit-skills.md`](docs/audit-skills.md). Project-level rule overrides live at `audit-rules/<name>.md`; per-run reports land in `audits/` (gitignored).

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
| `AI:Prices` | appsettings (see `appsettings.Example.json`) | US dollars per million tokens per model (`InputPerMillion` / `OutputPerMillion`), keyed by the model id (Anthropic) or deployment name (Azure OpenAI, Foundry) the usage rows carry; prices the `/assistant` usage line |
| `Trove:ApiKey` | Key Vault ref (prod) / appsettings (dev) | National Library of Australia API key — optional third-line ISBN lookup provider |

In prod, secret App Settings resolve via `@Microsoft.KeyVault(SecretUri=…)` references — the App Service's managed identity has `Key Vault Secrets User` on the vault. `deploy.ps1` writes `AuthClientSecret` + optionally `AIAnthropicApiKey` and `TroveApiKey` to the vault; `ai-services.bicep` writes `AIAzureOpenAIApiKey` via `listKeys()` against the newly-created OpenAI account.

Dev config templates: `appsettings.Example.json`, `appsettings.Development.Example.json`. Copy and fill in secrets. Only configure providers you want to use — the toggle auto-detects available providers.

## Infrastructure

- Docker Compose for local SQL Server 2022 Developer + Azurite (cover-storage blob emulator)
- GitHub Actions CI (build + test on PR), a Bookshelf compile check on PRs that touch it (`mobile-build.yml`), an on-demand Bookshelf APK build uploaded to a private container that Drew downloads from with his own Azure login (`bookshelf-apk.yml`), deploy-to-staging-slot, manual swap workflow, secret rotation
- Dependabot for NuGet (EF Core grouped) and npm (html5-qrcode)
- Azure Bicep templates under `infra/`. SQL, Key Vault, Azure OpenAI, and the cover-storage Blob account are reachable only via Private Endpoints; the App Service uses VNet integration + a peered eastus2 VNet to reach OpenAI. See `infra/README.md` for the full topology.
- **Slot-swap warmup** — `WEBSITE_SWAP_WARMUP_PING_PATH=/warmup` (set in `app-config.bicep`) makes Azure ping the dedicated `/warmup` endpoint during a slot swap, so the SQL pool + AAD token cold-start happens before promotion. Without the dedicated endpoint Azure would ping `/` which Easy Auth blocks (302 to login) and warm nothing.
- **Container start time limit** — `WEBSITES_CONTAINER_START_TIME_LIMIT=600` (Linux App Service default is 230s, too tight under cold AAD + CA-cert update). See the in-bicep comment for the timing breakdown.
- Auto-migration on startup (`TODO.md #21`: switch to deploy-time migration bundle when going multi-instance)

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
