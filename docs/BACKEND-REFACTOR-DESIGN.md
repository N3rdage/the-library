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
  **subtractive** (VMs don't touch EF), not additive.
- **Aggregating flat lookup tables** (`Tag`, `Publisher`, `Genre`). No invariants → stay plain data.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ BookTracker.Web  (Blazor host)                               │
│   Components/Pages/*.razor   →  inject ViewModel only        │
│   ViewModels/*               →  call Application ONLY        │
│   Api/*                      →  call Application ONLY        │
│   (DbContext referenced ONLY at the composition root)        │
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

### The one rule that buys the most

> **A ViewModel, a page, or an API endpoint must never create or touch a `DbContext`.
> They talk to `BookTracker.Application` and nothing below it.**

With `BookTracker.Application` as a **separate project**, the Web project does not reference
`BookTracker.Data`'s `DbContext` at all (except DI registration at the composition root). The
compiler enforces the seam — an agent *cannot* accidentally reach across it.

### Write side (commands)

A command is an intent. A handler orchestrates one transaction: load aggregate → call a
**method on the aggregate** → save. Business rules live on the aggregate, not the handler.

```csharp
// Application/Books/AddCopyToBook.cs
public sealed record AddCopyToBook(int BookId, int EditionId, Condition Condition, DateOnly? Acquired);

public sealed class AddCopyToBookHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task HandleAsync(AddCopyToBook cmd, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var book = await db.Books
            .Include(b => b.Editions).ThenInclude(e => e.Copies)
            .FirstOrDefaultAsync(b => b.Id == cmd.BookId, ct)
            ?? throw new NotFoundException(...);

        book.AddCopy(cmd.EditionId, cmd.Condition, cmd.Acquired); // ← invariant lives here

        await db.SaveChangesAsync(ct);
    }
}
```

### Read side (queries)

A query handler projects straight to a **read-model DTO** with `AsNoTracking()`. It must
**never** load a write aggregate to display it, and never return an EF entity to the UI.

```csharp
// Application/Books/GetBookDetail.cs
public sealed record GetBookDetail(int BookId);

public sealed class GetBookDetailHandler(IDbContextFactory<BookTrackerDbContext> dbFactory)
{
    public async Task<BookDetailView?> HandleAsync(GetBookDetail q, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Books.AsNoTracking()
            .Where(b => b.Id == q.BookId)
            .Select(b => new BookDetailView(/* flat projection */))
            .FirstOrDefaultAsync(ct);
    }
}
```

`GetCatalogSnapshot` (`Application/Catalog/`) is the template for read handlers.

### Rich aggregates (in BookTracker.Data)

Entities carry behaviour and have no public setters on invariant-bearing fields:

- `Book` (root) owns `Editions`; `Edition` owns `Copies`. Exposed as
  `IReadOnlyCollection<>`; mutated only through methods (`AddCopy`, `AddEdition`, …).
- Invariants live on the aggregate and are unit-testable.
- EF maps to **backing fields** for encapsulated collections (`UsePropertyAccessMode(Field)`).
- The `UpdatedAt` interceptor stays — it's a cross-cutting persistence concern, not a domain rule.

## Conventions

These are binding for all Application-layer work.

| # | Rule |
|---|------|
| C1 | ViewModels / pages / API endpoints never touch `DbContext`. They call Application handlers only. |
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
- **Transaction ownership.** Single `SaveChanges` per handler is the default; multi-aggregate
  writes (merges) use explicit `BeginTransaction`. No handler spans two aggregates without a
  transaction.
- **Exception → UI mapping** (open question). How `NotFoundException` / validation failures
  from handlers surface to the user (snackbar).

---

*Companion to `ARCHITECTURE.md`, which describes the system as built; this doc holds the
conventions and the reasons for them.*
