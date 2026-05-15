# Data dictionary

Semantic reference for the BookTracker catalogue. This document is the **interpretation layer** for the JSON snapshot consumed by the Claude Project "BookTracker Analysis" — it explains what every field *means*, what enum values *signify*, and which non-obvious mechanics (soft-delete, canonical-alias rollup, watermarks) shape the data you'll be looking at.

For the *shape* (how entities relate) see [`ARCHITECTURE.md`](../ARCHITECTURE.md) §Data model. For the curated genre taxonomy + provenance see [`GENRE-TAXONOMY.md`](GENRE-TAXONOMY.md). For the visual / palette system see [`STYLE-GUIDE.md`](STYLE-GUIDE.md).

---

## Conceptual layering — the four nouns

The model deliberately separates four concepts that other catalogues tend to conflate:

| Noun | Meaning | Example |
|------|---------|---------|
| **Work** | The abstract creative unit. A story, novel, play, poem. | *The Mist* (the Stephen King novella) |
| **Book** | The physical-object grouping. Contains one or more Works. | The volume titled *Skeleton Crew* (which contains *The Mist* + 21 other stories) |
| **Edition** | A specific printing of the Book. ISBN-keyed. | The 1985 Putnam hardcover *Skeleton Crew*, ISBN 0-399-13039-9 |
| **Copy** | The physical item you own. Condition-graded. | Drew's own slightly-foxed copy, acquired 2023-08, "Good" condition |

The same Work can appear inside multiple Books (a Christie short story reprinted across compendiums). Genres / authorship / series / first-publish-date live on the **Work**, not the Book — *Skeleton Crew* the volume is "horror" only because each contained story is. Reading state (Status, Rating, Notes, Tags) lives on the **Book**, not the Work — you finished the volume, not the abstract concept of every story it contains.

Single-Work books (a novel, the common case) get a 1:1 Book↔Work created automatically by the Add page.

---

## Book

The physical-object grouping. Carries reading state + cover art + Editions + Copies + Tags.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Title` | string (≤300, required) | For single-Work books, mirrors the sole Work's title. For compendiums, this is the *volume* title — distinct from Work titles inside. |
| `Category` | enum `BookCategory` | `Fiction` / `NonFiction`. Display-level only — orthogonal to genre. |
| `Status` | enum `BookStatus` | `Unread` / `Reading` / `Read`. Default `Read` (most Adds are post-read). **Semantically meaningless for reference books** (dictionaries, encyclopaedias, atlases) — convention is to leave them `Unread`. See TODO #51. |
| `Rating` | int 0–5 | 0 = unrated, 1–5 = stars. |
| `Notes` | string (nullable, MAX) | Personal reading notes ("recommended by X, finished in two days, didn't love the ending"). High-signal for analysis but personal. |
| `DateAdded` | DateTime (UTC) | When the row was created. |
| `UpdatedAt` | DateTime (UTC, indexed, default `GETUTCDATE()`) | Auto-bumped by `BookUpdatedAtInterceptor` on **any** change to this Book OR any aggregate child (Edition, Copy, Work, WorkAuthor, Tag join). Drives delta-sync to Bookshelf. Value converter pins `Kind=Utc` on read. |
| `DeletedAt` | DateTime? (UTC, filtered index) | Soft-delete tombstone. Non-null = hidden from every normal query via a global EF `HasQueryFilter`. Husk survives only so the catalog snapshot emits the Id in `deletedIds[]` for Bookshelf to drop locally. Aggregate children (Editions / Copies / joins) are *hard*-removed at delete time. The JSON snapshot **excludes** soft-deleted rows. |
| `DefaultCoverArtUrl` | string? (≤500) | Falls back to the first Edition's `CoverUrl` for display when not set. |
| `Editions` | List | 1-to-many. Most books have one Edition. |
| `Works` | List | M:N via join table `BookWork`. |
| `Tags` | List | M:N via join table `BookTag`. |

### Reading-state vocabulary

`Status` is read-state-of-the-volume. There's no per-Work read-state — a half-finished compendium doesn't track *which* stories you've read. If you need finer granularity, today's stopgap is Notes.

### Enums

```
BookStatus    = { Unread, Reading, Read }              ← default: Read
BookCategory  = { Fiction, NonFiction }                ← default: Fiction
```

---

## Work

The abstract creative unit. Authorship + subtitle + genres + series live here.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Title` | string (≤300, required) | The story / novel / play title. |
| `Subtitle` | string? (≤300) | Often used as the stopgap for "edition number" labels on reference books ("(3rd ed.)"), since `Edition` has no edition-number field. See TODO #51. |
| `FirstPublishedDate` | DateOnly? | The Work's first-publish date (distinct from any Edition's `DatePrinted` — *Hamlet* was first published c. 1603 regardless of when the paperback in your hand was printed). |
| `FirstPublishedDatePrecision` | enum `DatePrecision` | `Day` / `Month` / `Year`. Old works often only have a year. |
| `WorkAuthors` | List | M:N via explicit `WorkAuthor` join with `Order` field — canonical read source for ordered display ("Preston & Child"). |
| `Authors` | List (skip-nav) | M:N convenience navigation through `WorkAuthor`. **Does not preserve Order** — use `WorkAuthors` for display, this for set-membership semantics. |
| `Genres` | List | M:N via `GenreWork`. |
| `SeriesId` | int? | Optional FK. Null = standalone. |
| `SeriesOrder` | int? | Position in the Series (1-based). Currently `int` only — non-integer interquels (Stormlight #4.5 *Edgedancer*) sink to the bottom of the sort. See TODO #3 / #14. |
| `Books` | List | M:N via `BookWork`. |

### Multi-author works

Co-authored works (Preston & Child writing *Relic*) use `WorkAuthor` rows with `Order` = 0 for lead, 1+ for additional authors. Display surfaces (Library, search) join in order; aggregations join through skip-navigation.

### What's *not* on Work today

- **No `Role` enum** for `WorkAuthor` (Author / Editor / Translator / Illustrator). Edited references (Oxford Companion to X, encyclopaedias, anthologies, many cookbooks) currently get credited as authors and conflate with the genuine author rollup. See TODO #51.
- **No `EditionNumber` on `Edition`** (separate concept) — *Joy of Cooking 1975 vs 2019* is materially different content; today this lives in `Subtitle` or is implicit in `DatePrinted`. See TODO #51.

---

## Author

People (and, accidentally, the occasional institution) credited on Works. Self-referential for pen-name aliases.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Name` | string (≤200, **unique**, required) | Human-readable name as published. |
| `CanonicalAuthorId` | int? (self-FK) | **Null = canonical** (Stephen King). **Non-null = alias** pointing at the canonical (Richard Bachman → Stephen King). |
| `Aliases` | List | Inverse of `CanonicalAuthorId` — pen names that resolve to this Author. |
| `Works` | List (skip-nav) | M:N through `WorkAuthor` — works where this specific Author entity is credited (a Bachman novel's WorkAuthor is Bachman, not King). |
| `WorkAuthors` | List | Explicit join collection — useful when `Order` matters. |

### Canonical-alias rollup mechanic

This is the central semantic trick. A Work always points at the **specific** Author entity used at publication — a Bachman novel is *displayed* as "by Richard Bachman" because that's how it shipped. But aggregations (top-authors-by-count, author-page detail, etc.) group by `CanonicalAuthorId ?? Id`, so all Bachman titles roll up under King's tally. The `/authors` page manages this graph; find-or-create on save is auto (typing a fresh name silently creates a canonical Author).

### Corporate / institutional "authors"

The `Author` table is name-keyed and accepts anything. Encyclopaedias and reference works credited to "Encyclopaedia Britannica" or "Oxford University Press" land here as rows with no `CanonicalAuthorId` (no pen-name aliases for an institution) and look semantically odd. Minor wart. See TODO #51.

---

## Edition

A specific printing of a Book. ISBN-keyed.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `BookId` | int (FK) | Cascade-delete on Book removal. |
| `Isbn` | string? (≤20) | **Nullable + filtered unique** (`WHERE Isbn IS NOT NULL`). Pre-1974 books predate ISBN; arbitrarily many null-ISBN editions can coexist without index collision. |
| `Format` | enum `BookFormat` | See below. Default `TradePaperback`. |
| `DatePrinted` | DateOnly? | When this *specific printing* was published — not the Work's first-publish date. |
| `DatePrintedPrecision` | enum `DatePrecision` | Old prints often only give a year. |
| `CoverUrl` | string? (≤500) | URL into Azure Blob Storage `book-covers` container after mirror; was an upstream URL before `CoverMirrorBackgroundService` ran. |
| `IsUserSupplied` | bool (default false) | True = the cover is a photo Drew uploaded (typically because no online cover existed for a rare/old edition). Used to (a) flag with a "your photo" badge in display surfaces, (b) skip re-mirror passes that re-fetch upstream URLs. |
| `PublisherId` | int? (FK) | Optional. Delete-restricted (Publisher can't be deleted while Editions reference it). |
| `Copies` | List | 1-to-many. Cascade-delete on Edition removal. |

### Enums

```
BookFormat    = { Hardcover, TradePaperback, MassMarketPaperback, LargePrint }  ← default: TradePaperback
DatePrecision = { Day, Month, Year }                                              ← default: Day
```

### What `Edition` does *not* capture

- **Edition number** ("2nd edition", "Revised edition") — matters most for reference works (DSM-IV vs DSM-V; *Joy of Cooking 1975 vs 2019*). Stopgap: put "(3rd ed.)" in `Work.Subtitle`. See TODO #51.
- **Box-set membership** — multi-volume sets (Britannica's 24 volumes) are modelled today as a `Series(SeriesType=Series, ExpectedCount=24)` of single-Work Books, not as a first-class "Set" concept.

---

## Copy

The physical item Drew owns. Multi-copy support exists (a book given as a gift you also kept) but most Editions have exactly one Copy.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `EditionId` | int (FK) | Cascade-delete on Edition removal. |
| `Condition` | enum `BookCondition` | Standard used-book grading. Default `Good`. |
| `DateAcquired` | DateTime? | When this physical copy came into the collection. |
| `Notes` | string? | Per-copy notes (provenance, defects, "signed by author"). |

### Enums

```
BookCondition = { AsNew, Fine, VeryGood, Good, Fair, Poor }  ← default: Good
```

---

## Series

A named grouping that Works belong to. Two flavours.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Name` | string (≤300, **unique**, required) | Series name as recognised by the user. |
| `Author` | string? (≤200) | **Display-only string.** *Not* a FK — Series names with multiple credited authors (collaborations / shared universes) can list whatever's useful for display without forcing a single Author row. |
| `Type` | enum `SeriesType` | `Series` = numbered (Ender's Game, Stormlight); `Collection` = loose grouping (Discworld, Hercule Poirot). |
| `ExpectedCount` | int? | For numbered series — the total volumes. Null for Collections / unknown / open-ended. Drives series-gaps detection ("you have 1, 2, 3, 5 — missing 4"). |
| `Description` | string? | Free text. |
| `Works` | List | 1-to-many. Inverse of `Work.SeriesId`. |

### Series membership is per-Work

A Christie short-story compendium (Book) doesn't itself belong to the Poirot series — its constituent Stories (Works) do. This matters for analysis: counting "books in series X" by joining `Book → Work → Series` may double-count compendiums.

---

## Genre

Hierarchical taxonomy. See [`GENRE-TAXONOMY.md`](GENRE-TAXONOMY.md) for the full tree + provenance.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Name` | string (≤100, **unique**, required) | |
| `ParentGenreId` | int? (self-FK) | Null for top-level. Delete-restricted (can't drop a parent while children exist). |
| `Children` | List | Inverse navigation. |
| `Works` | List | M:N via `GenreWork`. |

### Selection semantics

Selecting a sub-genre on the Add / Edit page **auto-selects its parent** — a Work tagged "Cthulhu Mythos" is also tagged "Horror" implicitly via the join. Aggregations should respect this when filtering.

---

## Tag

Free-form labels on Books (not Works). Distinct from Genre — tags are user-defined, flat, and applied at the volume level.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Name` | string (≤100, **unique**, required) | |
| `Books` | List | M:N via `BookTag`. |

Seeded with one row: `follow-up` (Id = 1) — used to flag books needing later attention.

---

## Publisher

Imprint / publishing house. Optional FK target from Edition.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Name` | string (≤200, **unique**, required) | |
| `Editions` | List | 1-to-many. Restrict-delete (Publisher can't be deleted while Editions reference it). |

---

## WishlistItem

Books Drew wants to acquire. Separate entity (not a Book with a Status enum value) because wishlist rows often lack the full metadata of a captured book.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Title` | string (≤300, required) | |
| `Author` | string (≤200, required) | Free-text — not a FK to Author. |
| `Priority` | enum `WishlistPriority` | `Low` / `Medium` / `High`. Default `Medium`. |
| `Price` | decimal? (10,2) | Optional expected price. |
| `DateAdded` | DateTime | |
| `Isbn` | string? (≤20, indexed) | For in-store ISBN-scan lookup. |
| `SeriesId` | int? (FK) | Optional link to a known Series. Set-null on Series delete. |
| `SeriesOrder` | int? | Position in the series this item would fill. |

### Enums

```
WishlistPriority = { Low, Medium, High }  ← default: Medium
```

---

## MaintenanceLog

Marker rows for one-shot data operations that should run at most once per deployment (typically a hosted startup task that backfills data after a schema/semantic change).

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `Name` | string (≤200, **unique**, required) | Operation identifier (e.g. `BackfillEditionFormats-v1`). Duplicate startup is a no-op. |
| `CompletedAt` | DateTime | |
| `Notes` | string? (≤2000) | |

Not typically interesting for analysis — included for completeness.

---

## IgnoredDuplicate

User-dismissed duplicate-candidate pairs so `/duplicates` doesn't keep resurfacing false positives.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | int (PK) | |
| `EntityType` | enum `DuplicateEntityType` | `Author` / `Work` / `Book` / `Edition`. Polymorphic — no FK to the referenced entity. |
| `LowerId` | int | The smaller of the pair's two IDs. |
| `HigherId` | int | The larger. Normalised so (A,B) and (B,A) hit the same row. |
| `DismissedAt` | DateTime | |
| `Note` | string? (≤1000) | |

Stale rows (where either side no longer exists) are filtered out lazily by `DuplicateDetectionService`.

---

## Join tables (shape-only)

These exist as conventional EF M:N joins — no extra fields. The JSON snapshot resolves M:N relationships into nested arrays so you don't see them as separate top-level lists.

- `BookWork` — `(BookId, WorkId)`
- `BookTag` — `(BookId, TagId)`
- `GenreWork` — `(GenreId, WorkId)`
- `WorkAuthor` — `(WorkId, AuthorId, Order)` — **carries `Order` field**, distinct from the others

---

## Mechanics worth remembering when reasoning about the data

1. **Soft-delete is one-way at the husk level.** Deleting a Book leaves a tombstone row with `DeletedAt` set; Editions / Copies / joins under it are *hard*-removed. The JSON snapshot excludes tombstoned rows entirely — analysis sees only live data.

2. **`UpdatedAt` is aggregate-watermarked.** Editing an Edition or adding a Tag bumps the parent Book's `UpdatedAt`. Don't use it as a "Book row last edited" signal — it's "anything in the Book aggregate last touched."

3. **Author rollup is opt-in.** Joining Work → Author gets you the as-published name. Joining through `CanonicalAuthorId ?? Id` gets you the rolled-up identity. Pick one consciously per query.

4. **Genres are hierarchical and auto-cascading on selection** — a Work tagged "Urban Fantasy" should also have "Fantasy" in its genres set. If the data doesn't show that, it's a capture-time bug, not a model feature.

5. **`Edition.Isbn` uniqueness is filtered** — count distinct ISBNs only over `WHERE Isbn IS NOT NULL`, otherwise you'll over-count the null bucket.

6. **`Book.Status` is meaningless for reference books** by convention. Don't infer "Drew hasn't read this dictionary" from `Status = Unread`.

7. **`Series.Author` is a string, not a FK.** Don't try to join it to the Author table — it's display-only.

8. **`WishlistItem.Author` is also a string, not a FK** — same reason. Wishlist rows are looser than captured Books and may predate canonical Author rows.

---

## Known gaps surfaced by capture experience

Tracked in [`TODO.md`](../TODO.md):

- **#51 — Reference book capture nuances:** editor role on `WorkAuthor`, `EditionNumber` on `Edition`, `BookStatus.Reference` value, multi-volume sets as a first-class concept, corporate-author handling.
- **#3 / #14 — Non-integer `SeriesOrder`:** Stormlight #4.5 *Edgedancer* and other interquels can't be expressed; today they sink to the bottom of the cluster.
- **#13 — Multi-series membership:** a Discworld novel can't currently belong to both "Discworld" and "Discworld: City Watch" sub-series.
- **#47 — eBook format:** `BookFormat` is physical-only; digital titles aren't modelled.

When proposing data changes during analysis, flag if the change would benefit from one of these schema gaps being closed first.
