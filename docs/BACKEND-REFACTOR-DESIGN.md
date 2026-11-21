# Back-end Architecture — Design & Conventions

> Every change to the Application layer — and every agent that touches it — conforms to the
> rules in [Conventions](#conventions). If a change can't be made within these rules, the rules
> change here first (in a doc PR), not silently in code.

## Why this exists

The layer guards against three smells a service + MVVM architecture drifts into:

1. **Anemic domain model.** Entities as EF property-bags with public setters, with every
   invariant (`UpdatedAt` bump, "can't delete a Book that has Copies", series-order flooring)
   living *outside* the entity as procedural code in services/ViewModels.
2. **Fuzzy ViewModel ↔ Service boundary.** ViewModels *and* services both holding
   `IDbContextFactory` and running EF directly, so data access has no single owner and logic
   smears across both layers — the "where does this go?" anxiety.
3. **No formalised read/write split.** Interactive pages re-querying tracked entity graphs and
   projecting ad hoc instead of reading a dedicated read-model.

## Goals (in priority order)

The design optimises for, co-equally:

- **Bounded blast radius / agent-legibility.** *The* headline goal. Changes should be local
  in fact, not just in appearance. Seams should be **compiler-enforced** (project boundaries,
  record-typed contracts) so a breaking change fails at `dotnet build`, not at runtime three
  pages away. Folders should map **1:1 to features** so "change book editing" is one folder.
- **Testability.** Domain logic (invariants, calculations) must be unit-testable with **zero
  EF / no SQL container**.
- **Maintainability / clarity.** One obvious home for each kind of logic.
- **Craft / learning.** Build the pattern well.

## Non-goals (explicitly out of scope)

We are taking the **spine** of DDD + CQRS, not the cathedral. Out of scope:

- **MediatR.** No external mediator dependency and no pipeline-behaviour stage. We *do* have
  a **thin hand-rolled `IDispatcher`** (~40 lines we own, `Application/Messaging/`) so a
  consumer injects one dispatcher instead of one handler per command (injecting handlers
  directly pushes a ViewModel like `BookDetailViewModel` to ~10 ctor args). It has no behaviours; if
  cross-cutting logging/validation/transactions are ever wanted, that one class is where they'd
  go. The line we're holding is "no framework magic we didn't write," not "no dispatcher."
- **A separate domain model vs persistence model.** The EF entities *are* the aggregates.
  We do not maintain a parallel POCO domain + mapping layer — that tax isn't worth it solo.
- **Event sourcing / domain events infrastructure.** Not now.
- **A separate read database / materialised store.** The read side is `AsNoTracking()`
  projections against the same DB. CQRS here means *separate models*, not separate storage.
- **MVVM ceremony on Blazor.** No `INotifyPropertyChanged` / observable-property style.
  Blazor components are the view; ViewModels are the VM. The MVVM rule here is
  **subtractive** (VMs don't touch EF), not additive. No ViewModel or component touches EF
  (see [The one rule that buys the most](#the-one-rule-that-buys-the-most)).
- **Aggregating flat lookup tables** (`Tag`, `Publisher`, `Genre`). No invariants → stay plain data.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ BookTracker.Web  (Blazor host)                               │
│   Components/Pages/*.razor   →  inject ViewModel only        │
│   ViewModels/*               →  call Application ONLY        │
│   Api/*                      →  call Application ONLY        │
│   (DbContext: composition root + the C1 exceptions only)     │
└───────────────┬─────────────────────────────────────────────┘
                │ depends on
┌───────────────▼─────────────────────────────────────────────┐
│ BookTracker.Application                                      │
│   Books/        Works/      Series/      Wishlist/   ...     │
│     each feature folder holds:                              │
│       • Command records + handlers   (write)                │
│       • Query records + handlers     (read)                 │
│       • Read-model DTOs              (read output shapes)    │
│   depends on: BookTracker.Data, BookTracker.Shared          │
└───────────────┬─────────────────────────────────────────────┘
                │ depends on
┌───────────────▼─────────────────────────────────────────────┐
│ BookTracker.Data   (entities are RICH aggregates)           │
│   Models/   — private setters, encapsulated collections,    │
│              factory + guard methods, behaviour ON entity   │
│   BookTrackerDbContext, Migrations, Interceptors            │
│   EF mapped to backing fields where collections encapsulated│
└─────────────────────────────────────────────────────────────┘
```

The code matches the diagram apart from the C1 exceptions named below;
[Rich aggregates](#rich-aggregates-in-booktrackerdata) below describes the Data layer.

### The one rule that buys the most

> **A ViewModel, a page, a component, or an API endpoint must never create or touch a
> `DbContext`. They talk to `BookTracker.Application` and nothing below it.**

No ViewModel, page or component in `BookTracker.Web` touches `DbContext`. Five files in the
Web project do, and they are the named exceptions in C1:

- `ProgramSetup.cs` — the composition root: it registers `AddDbContextFactory<BookTrackerDbContext>`
  and runs the Development-only migrate-on-startup.
- `Api/WarmupEndpoints.cs` — the anonymous `/warmup` probe, which runs one trivial query to warm
  the SQL connection pool before a slot swap.
- `Services/Covers/CoverMirrorBackgroundService.cs` and its helper
  `Services/Covers/CoverMirrorQueue.cs` — the cover-mirror hosted service and its due-row
  queries and `CoverMirrorState` writes.
- `Services/EditionFormatBackfillService.cs` — the one-shot startup backfill of Edition formats.

The compiler does not enforce the rule: `BookTracker.Web.csproj` references `BookTracker.Data`
directly (the composition root and the Data enums need it), so a new
`IDbContextFactory<BookTrackerDbContext>` injection in Web compiles.
`BookTracker.Tests/Architecture/WebLayeringTests.cs` enforces it instead, so an agent that
reaches across the seam fails the unit tests: only those five Web types may hold a `DbContext`,
its factory or a `DbSet<>`, and the same test keeps Data entities out of ViewModels and
components.

### Write side (commands)

A command is an intent. A handler orchestrates one transaction: load aggregate → call a
**method on the aggregate** → save. Business rules live on the aggregate, not the handler.

```csharp
// Application/Books/AddCopyToEdition.cs
public sealed record AddCopyToEdition(int EditionId, BookCondition Condition, DateTime? DateAcquired, string? Notes) : ICommand<int>;

public sealed class AddCopyToEditionHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : ICommandHandler<AddCopyToEdition, int>
{
    public async Task<int> HandleAsync(AddCopyToEdition command, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var edition = await db.Editions.FindAsync([command.EditionId], ct)
            ?? throw new NotFoundException($"Edition {command.EditionId} not found.");

        var copy = edition.AddCopy(command.Condition, command.DateAcquired, command.Notes); // ← invariant lives here
        await db.SaveChangesAsync(ct);
        return copy.Id;
    }
}
```

### Read side (queries)

A query handler projects straight to a **read-model DTO** with `AsNoTracking()`. It must
**never** load a write aggregate to display it, and never return an EF entity to the UI.

```csharp
// Application/Books/GetBookDetail.cs
public sealed record GetBookDetail(int BookId) : IQuery<BookDetail?>;

public sealed class GetBookDetailHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
    : IQueryHandler<GetBookDetail, BookDetail?>
{
    public async Task<BookDetail?> HandleAsync(GetBookDetail query, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .AsNoTracking()
            .Where(b => b.Id == query.BookId)
            .Select(b => new { /* only the scalars and small lists the page shows */ })
            .FirstOrDefaultAsync(ct);
        // ... formats the projection into the BookDetail record (abridged)
    }
}
```

`GetCatalogSnapshot` (`Application/Catalog/`) is the template for read handlers.

### Rich aggregates (in BookTracker.Data)

Entities carry their behaviour, and their invariant-bearing fields have private setters, so every
write goes through a method and the compiler enforces it. Every entity collection is
`IReadOnlyCollection<T>` over a private backing field (C7):

- `Book`'s scalar fields (title, category, status, rating, notes, cover, series membership) are
  written only through its methods (`Create`, `Rate`, `MarkRead`, `UpdateDetails`, `SetCover`,
  `AssignToSeries`, `SoftDelete`, …): the setters are private, `UpdatedAt` / `DeletedAt` internal
  for the interceptor and `SoftDelete`, and the compiler enforces it. `BookTracker.Tests` alone
  sees the internals (to seed a watermark or a tombstone); `BookTracker.Application` does not.
  Its collections (`Editions`, `Tags`, `Works`, `BookWorks`) are written only through its
  methods: `AddEdition`, `RemoveCopy` and `MoveEditionsTo` (the book merge) for editions,
  `AddTag`, `RemoveTag` and `AdoptTagsFrom` (the book merge) for tags, `AttachWork` and
  `Work.RemoveFrom` for the work links, and `SoftDelete`. The tests' seed builder `TestBooks`
  stores seeded collections as given through the internal `SeedCollections`.
- `Edition`'s and `Copy`'s scalar fields are written only through their methods, and the
  compiler enforces it: the setters are private (`Id` stays public, as on `Book`), as are the
  foreign keys `Edition.BookId` and `Copy.EditionId`.
  `Edition` has `UpdateDetails`, `SetCover`, `ReplaceCoverUrl` (the cover mirror's URL swap,
  which leaves `IsUserSupplied` alone), `ChangeFormat`, `ReassignPublisher`, `MoveToBook`,
  `FillBlanksFrom` and `AdoptCopiesFrom` (the edition merge), `AddCopy` and `RemoveCopy`;
  `Copy` has `UpdateDetails`, `LendTo` and `MarkReturned`. Each has a private parameterless
  constructor for EF and an internal one that stores the values as given, used by
  `book.AddEdition` / `edition.AddCopy` (which normalise first) and by the tests' seed
  builder `TestEditions`. The parent navigations `Edition.Book` and `Copy.Edition` have
  internal setters: EF fixes them up from the collections and keys, and the tests attach a
  detached seed to its parent through them; `BookTracker.Application` cannot set them.
- `Work`'s scalar fields and `WorkAuthor` rows are written only through `Work`'s methods (`Create`,
  `UpdateDetails`, `SetFirstPublished`, `FillBlanksFrom`, `SetGenres`, `AssignAuthorship`,
  `AdoptGenresFrom`, `AppearsIn`, `RemoveFrom`, `LeaveAllBooks`), and the compiler enforces it: the
  scalar setters are private (`Id` stays public, as on `Book`), and `WorkAuthor.Order` and `Role`
  are set only by `AssignAuthorship`. `Work`'s collections (`WorkAuthors`, `Authors`, `Genres`,
  `Books`, `BookWorks`) are `IReadOnlyCollection<T>` over private backing fields; `Book.AttachWork`
  adds the join row to the Work's end through the internal `AddBookWork`. Each has a private parameterless constructor for EF and an
  internal one that stores the values as given, used by the tests' seed builder `TestWorks` (and,
  for `WorkAuthor`, by `AssignAuthorship`).
- `Series`' fields are written only through `Create` and `UpdateDetails`, which keep the
  ExpectedCount/Type pairing; name uniqueness stays in the handlers (`SeriesNameGuard`).
  `WishlistItem`'s fields are written only through `Create`, `CreateSeriesSlot` (which links the
  item to its series and slot) and `SetIsbns` (the ISBN dual-write). The setters are private on
  both (`Id` stays public, as on `Book`); `Series.Books` membership is written
  through `Book.AssignToSeries` / `ClearSeries`. Each has a private parameterless constructor for
  EF and an internal one that stores the values as given, used by the tests' seed builders
  `TestSeries` and `TestWishlistItems`.
- `Book` (root) owns `Editions`; `Edition` owns `Copies`; `Copy` owns `Loans`. Exposed as
  `IReadOnlyCollection<>`; mutated only through methods (`AddCopy`, `AddEdition`, `LendTo`, …).
  The inverse collections on `Author`, `Genre`, `Publisher` and `Tag` are read-only too: EF
  fills them, and writes go through the owning side (`Work.AssignAuthorship`, `Work.SetGenres`,
  `Edition.ReassignPublisher`, `Book.AddTag`).
- Invariants live on the aggregate and are unit-testable.
- EF maps every collection navigation, skip navigations included, to its **backing field**
  (`HasField` + `UsePropertyAccessMode(Field)`). `BookTracker.Tests/Architecture/EntityCollectionTests.cs`
  fails when an entity exposes a collection as anything but `IReadOnlyCollection<>` /
  `IEnumerable<>` or with a public setter.
- The `UpdatedAt` interceptor is a cross-cutting persistence concern, not a domain rule.

Every handler, the five merges included, writes through these methods: a merge calls the
entity methods named above (for example `MergeBooks` calls `Book.UpdateNotes`, `SetCover`, `Rate`,
`AssignToSeries`, `AttachWork`, `AdoptTagsFrom`, `MoveEditionsTo` and `SoftDelete`), and none
assigns a field directly. The invariants are unit-tested under `BookTracker.Tests/Domain/`.

## Conventions

These are binding for all Application-layer work.

| # | Rule |
|---|------|
| C1 | ViewModels / pages / components / API endpoints never touch `DbContext`. They call Application handlers only. `WebLayeringTests` (`BookTracker.Tests/Architecture/WebLayeringTests.cs`) enforces it. In `BookTracker.Web` exactly five files may touch it: `ProgramSetup.cs` (composition root), `Api/WarmupEndpoints.cs`, `Services/Covers/CoverMirrorBackgroundService.cs`, `Services/Covers/CoverMirrorQueue.cs` and `Services/EditionFormatBackfillService.cs`. |
| C2 | One feature = one folder under `BookTracker.Application/<Feature>/`. Commands, queries, handlers, and read-model DTOs for that feature live together. |
| C3 | Every cross-boundary contract is a `record` (commands, queries, read-model views). No EF entity crosses out of Application to the UI. |
| C4 | One handler per file. File name = command/query name. Handlers are `sealed`. |
| C5 | Read handlers use `AsNoTracking()` and project to a DTO. Write handlers load a tracked aggregate, mutate via aggregate methods, `SaveChanges`. Never mix in one handler. |
| C6 | Invariants live **on the aggregate**, not in the handler. Handlers orchestrate; aggregates enforce. |
| C7 | Entity collections are encapsulated: `IReadOnlyCollection<>` over a private backing field that EF maps with `UsePropertyAccessMode(Field)`, written only through the entity's methods. No entity exposes a `List<>` / `ICollection<>` or a collection setter; `EntityCollectionTests` (`BookTracker.Tests/Architecture/EntityCollectionTests.cs`) enforces it. |
| C8 | Domain unit tests cover every invariant/calculation with **no EF / no container**. Handlers get integration tests against the MSSQL Testcontainer. |
| C9 | Flat lookup tables with no invariants (`Tag`, `Publisher`, `Genre`) stay plain — do not over-aggregate. |
| C10 | Commands model **user intentions/gestures, not table columns**. A gesture that sets several fields at once is *one* command (e.g. `MarkBookRead` = status + rating + notes), not three — atomic, one save, one `UpdatedAt` bump. A field-level command is correct only when the UI fires that field on its own (e.g. the detail page's independent inline rating / status / notes auto-saves). Never reflexively emit a CRUD command per property. |
| C11 | An aggregate may **own a shared many-to-many relationship and self-manage its lifecycle by ref count**. `Work` owns Book↔Work: it's born attached to its first book (the `Create` factory), gains/loses books via `AppearsIn`/`RemoveFrom`, and is orphaned at ref count 0. Route *all* relationship mutations through the owning aggregate's methods (don't `book.Works.Add` in one place and `work.AppearsIn` in another). The orphan-delete is **signalled** by the aggregate (`RemoveFrom` returns "now orphaned") but **executed** by the handler (`db.Remove`) — the same split as a soft-delete. Relationship soft-outcomes (already-attached, target gone) stay **nullable returns**, distinct from a `NotFoundException` (which is for a mutated *root* that's gone). |
| C12 | **Construct aggregates + their children through factories/methods, never raw `new` in a handler.** A handler that creates an aggregate or adds a child routes through the seam that owns the invariant — `Work.Create`, `book.AddEdition` (which seeds the mandatory first `Copy` and normalises the ISBN) — not `new Work { … }` / `new Edition { Copies = [new Copy …] }`. Raw construction in a handler silently *forks* the invariant: the next change to the aggregate's defaults or guards won't reach that site, and the next reader can't tell whether bypassing the factory was deliberate. This is the corollary of C6 (invariants live on the aggregate) for the **creation** path. |

## Registration, transactions and errors

- **Handler registration.** Handlers implement
  `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResult>`; commands carry an
  `ICommand` / `ICommand<TResult>` marker; consumers inject a Scoped `IDispatcher` that
  resolves the handler by command type. **Registration is a convention scan** —
  `AddApplicationLayer` walks the assembly and registers every `ICommandHandler<>` implementer
  against its closed interface, so implementing the interface *is* the registration and a
  handler can't be forgotten. An explicit list's forgot-to-register footgun outweighs its
  one-file greppability, and `grep ": ICommandHandler<"` recovers the same list. No attribute
  (the interface is already the marker), no MediatR.
- **Transaction ownership.** The rule: a command handler's writes go through one
  `SaveChanges`, or through an explicit transaction when there is more than one, so they commit
  or fail together. Every command handler makes at most one `SaveChangesAsync` call
  (`CreateSeries` and `UpdateSeries` make it through `SeriesNameGuard.SaveTranslatingDuplicateAsync`),
  and EF Core runs one `SaveChanges` as one transaction. Several handlers write to more than one
  aggregate in that one call, for example `DeleteBook` and
  `RemoveWorkFromBook` (the Book's Work membership and any Work it orphans), `AttachWorkToBook`
  (Book and Work membership), `CreateWorkOnBook` and `AttachWorksToBook` (new Works on a Book,
  plus find-or-created Authors), `UpdateWork` (the Work plus find-or-created Authors),
  `MarkWishlistItemBought` (WishlistItem, Book, Work, Tag, Author), `AddEditionToBook` and
  `UpdateEdition` (plus a find-or-created Publisher), `AddTagToBook` (plus a find-or-created Tag).
  The five merges (`MergeBooks`, `MergeWorks`, `MergeAuthors`, `MergeEditions`,
  `MergePublishers`) also open an explicit `BeginTransactionAsync` around their single save. No
  handler dispatches another command or saves twice.
- **Exception → UI mapping.** Handlers throw `NotFoundException` when the root they mutate is
  gone; the aggregates (`Book`, `Work`, `Copy`, `Series`, `WishlistItem`, `Author`), the handlers
  `CreateBook` and `AttachWorksToBook`, and `SeriesNameGuard` throw `DomainRuleException` for a
  broken rule; soft outcomes come back as nullable returns or result records. A
  `DomainRuleException` or `NotFoundException` from a dispatch is an expected outcome: every view
  model that takes `IDispatcher` derives from `DispatchingViewModel` (enforced by
  `DispatchingViewModelLayeringTests`), whose `DispatchAsync` clears `Error`, runs the dispatch,
  and turns those two exception types into `Error` plus a `false` (or default) result. The
  component renders `Error` through the shared `ActionError` alert where the user acted, and
  snackbars are for successes only. Any other exception is a fault: it propagates to
  `PageErrorBoundary` in `MainLayout`, which logs it with a trace id and shows one page-level
  message that keeps the circuit alive.

---

*Companion to `ARCHITECTURE.md`, which describes the system as built; this doc holds the
conventions and the reasons for them.*
