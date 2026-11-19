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

The Web layer matches the diagram apart from the C1 exceptions named below. The Data layer's
encapsulation does not match it yet; [Rich aggregates](#rich-aggregates-in-booktrackerdata)
below says where.

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

The target is entities that carry behaviour and have no public setters on invariant-bearing
fields:

- `Book` (root) owns `Editions`; `Edition` owns `Copies`. Exposed as
  `IReadOnlyCollection<>`; mutated only through methods (`AddCopy`, `AddEdition`, …).
- Invariants live on the aggregate and are unit-testable.
- EF maps to **backing fields** for encapsulated collections (`UsePropertyAccessMode(Field)`).
- The `UpdatedAt` interceptor stays — it's a cross-cutting persistence concern, not a domain rule.

The code meets the behaviour half: `Book`, `Edition` and `Work` carry the mutator methods
(`Book.AddEdition`, `Edition.AddCopy`, `Work.Create`, `Work.RemoveFrom`, …), most handlers go
through them, and the invariants are unit-tested under `BookTracker.Tests/Domain/`. It does not
meet the encapsulation half yet: every property on `Book`, `Edition`, `Copy` and `Work` still has
a public setter, their collections are public `List<>` properties, no backing fields are mapped,
and the merge handlers assign fields directly. Closing that gap (C7) is tracked in the backlog.

## Conventions

These are binding for all Application-layer work. C7 is also a target that existing code
does not meet yet (see Rich aggregates above); the gap is tracked in the backlog, and new code
must not widen it.

| # | Rule |
|---|------|
| C1 | ViewModels / pages / components / API endpoints never touch `DbContext`. They call Application handlers only. `WebLayeringTests` (`BookTracker.Tests/Architecture/WebLayeringTests.cs`) enforces it. In `BookTracker.Web` exactly five files may touch it: `ProgramSetup.cs` (composition root), `Api/WarmupEndpoints.cs`, `Services/Covers/CoverMirrorBackgroundService.cs`, `Services/Covers/CoverMirrorQueue.cs` and `Services/EditionFormatBackfillService.cs`. |
| C2 | One feature = one folder under `BookTracker.Application/<Feature>/`. Commands, queries, handlers, and read-model DTOs for that feature live together. |
| C3 | Every cross-boundary contract is a `record` (commands, queries, read-model views). No EF entity crosses out of Application to the UI. |
| C4 | One handler per file. File name = command/query name. Handlers are `sealed`. |
| C5 | Read handlers use `AsNoTracking()` and project to a DTO. Write handlers load a tracked aggregate, mutate via aggregate methods, `SaveChanges`. Never mix in one handler. |
| C6 | Invariants live **on the aggregate**, not in the handler. Handlers orchestrate; aggregates enforce. |
| C7 | Aggregate-owned collections are encapsulated (`IReadOnlyCollection<>` + mutator methods + EF backing fields). No public `List<>` setters on roots. |
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
- **Exception → UI mapping (open question).** How a failed command or query reaches the user
  is undecided; a consistent design is pending Drew's choice, and callers stay as they are until
  then. Today: handlers throw `NotFoundException` when the root they mutate is
  gone; aggregates (`Book`, `Work`, `Series`, `WishlistItem`) and `SeriesNameGuard` throw
  `DomainRuleException` for a broken rule; soft outcomes come back as nullable returns or result
  records. There is no central mapping: each caller catches what it expects. The edit-dialog
  ViewModels (`BookEditDialogViewModel`, `WorkEditDialogViewModel`, `EditionFormDialogViewModel`,
  `CopyFormDialogViewModel`), `SeriesEditViewModel` and `BookDetailViewModel`'s cover upload catch
  `NotFoundException` and turn it into a no-op, a failure result or a not-found state;
  `SeriesEditViewModel` and `AIAssistantViewModel` (creating a series from a suggestion) show a
  `DomainRuleException`'s message inline; and many pages and dialogs wrap their save calls in a
  `catch (Exception)` that shows the message in a MudBlazor snackbar.

---

*Companion to `ARCHITECTURE.md`, which describes the system as built; this doc holds the
conventions and the reasons for them.*
