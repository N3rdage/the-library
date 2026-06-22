# Back-end Architecture Refactor — Design & Conventions (PR0)

> **Status:** Proposed / design phase. This is the *brief* the whole refactor arc follows.
> Every later PR — and every agent that touches this work — should conform to the rules
> in [Conventions](#conventions). If a change can't be made within these rules, the rules
> change here first (in a doc PR), not silently in code.

## Why this exists

The back-end *feels* like "code-behind / ball of mud," but a structural read shows it
isn't: there is **no code-behind** (components inject ViewModels only), **27 ViewModels**,
**31 services (~4k LOC)** organised by domain, API endpoints that go through services, and
`CatalogSnapshot` — already a denormalised, EF-free, read-only projection (a CQRS read-model
in all but name).

So this arc is **not** a rescue from chaos. It's a targeted upgrade of a working service +
MVVM architecture to fix three real smells:

1. **Anemic domain model.** Entities are EF property-bags with public setters. All behaviour
   and every invariant (`UpdatedAt` bump, "can't delete a Book that has Copies", series-order
   flooring) lives *outside* the entity, as procedural code in services/ViewModels.
2. **Fuzzy ViewModel ↔ Service boundary.** ViewModels *and* services both hold
   `IDbContextFactory` and run EF directly. Data access has no single owner, so logic smears
   across both layers. This is the "where does this go?" anxiety.
3. **No formalised read/write split.** Interactive pages re-query tracked entity graphs and
   project ad hoc. `CatalogSnapshot` proves we know how to build a read-model; we just don't
   do it consistently.

## Goals (in priority order)

This refactor optimises for, co-equally:

- **Bounded blast radius / agent-legibility.** *The* headline goal. Changes should be local
  in fact, not just in appearance. Seams should be **compiler-enforced** (project boundaries,
  record-typed contracts) so a breaking change fails at `dotnet build`, not at runtime three
  pages away. Folders should map **1:1 to features** so "change book editing" is one folder.
- **Testability.** Domain logic (invariants, calculations) must be unit-testable with **zero
  EF / no SQL container**.
- **Maintainability / clarity.** One obvious home for each kind of logic.
- **Craft / learning.** Build the pattern well; mine it for a retro + blog candidates at arc close.

## Non-goals (explicitly out of scope)

We are taking the **spine** of DDD + CQRS, not the cathedral. Out of scope:

- **MediatR.** No external mediator dependency and no pipeline-behaviour stage. We *do* have
  a **thin hand-rolled `IDispatcher`** (~35 lines we own, `Application/Messaging/`) so a
  consumer injects one dispatcher instead of one handler per command — added at the PR1b gate
  when `BookDetailViewModel`'s ctor hit 10 args (see retro). It has no behaviours; if
  cross-cutting logging/validation/transactions are ever wanted, that one class is where they'd
  go. The line we're holding is "no framework magic we didn't write," not "no dispatcher."
- **A separate domain model vs persistence model.** The EF entities *are* the aggregates.
  We do not maintain a parallel POCO domain + mapping layer — that tax isn't worth it solo.
- **Event sourcing / domain events infrastructure.** Not now.
- **A separate read database / materialised store.** The read side is `AsNoTracking()`
  projections against the same DB. CQRS here means *separate models*, not separate storage.
- **MVVM ceremony on Blazor.** No `INotifyPropertyChanged` / observable-property style.
  Blazor components are the view; existing ViewModels are the VM. The MVVM fix here is
  **subtractive** (VMs stop touching EF), not additive.
- **Aggregating flat lookup tables** (`Tag`, `Publisher`, `Genre`). No invariants → stay plain data.

## Target architecture

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
│ BookTracker.Application   ← NEW PROJECT                      │
│   Books/        Works/      Series/      Wishlist/   ...     │
│     each feature folder holds:                              │
│       • Command records + handlers   (write)                │
│       • Query records + handlers     (read)                 │
│       • Read-model DTOs              (read output shapes)    │
│   depends on: BookTracker.Data, BookTracker.Shared          │
└───────────────┬─────────────────────────────────────────────┘
                │ depends on
┌───────────────▼─────────────────────────────────────────────┐
│ BookTracker.Data   (entities are now RICH aggregates)       │
│   Models/   — private setters, encapsulated collections,    │
│              factory + guard methods, behaviour ON entity   │
│   BookTrackerDbContext, Migrations, Interceptors            │
│   EF mapped to backing fields where collections encapsulated│
└─────────────────────────────────────────────────────────────┘
```

### The one rule that buys the most

> **A ViewModel, a page, or an API endpoint must never create or touch a `DbContext`.
> They talk to `BookTracker.Application` and nothing below it.**

With `BookTracker.Application` as a **separate project**, the Web project will not reference
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

`CatalogSnapshotService` is *already* this shape — it relocates into `Application/Catalog/`
near-unchanged and becomes the template for read handlers.

### Rich aggregates (in BookTracker.Data)

Entities gain behaviour and lose public setters on invariant-bearing fields:

- `Book` (root) owns `Editions`; `Edition` owns `Copies`. Exposed as
  `IReadOnlyCollection<>`; mutated only through methods (`AddCopy`, `AddEdition`, …).
- Invariants currently enforced procedurally move onto the aggregate and become unit-testable.
- EF maps to **backing fields** for encapsulated collections (`UsePropertyAccessMode(Field)`).
- The `UpdatedAt` interceptor stays — it's a cross-cutting persistence concern, not a domain rule.

## Conventions

These are binding for the whole arc.

| # | Rule |
|---|------|
| C1 | ViewModels / pages / API endpoints never touch `DbContext`. They call Application handlers only. |
| C2 | One feature = one folder under `BookTracker.Application/<Feature>/`. Commands, queries, handlers, and read-model DTOs for that feature live together. |
| C3 | Every cross-boundary contract is a `record` (commands, queries, read-model views). No EF entity crosses out of Application to the UI. |
| C4 | One handler per file. File name = command/query name. Handlers are `sealed`. |
| C5 | Read handlers use `AsNoTracking()` and project to a DTO. Write handlers load a tracked aggregate, mutate via aggregate methods, `SaveChanges`. Never mix in one handler. |
| C6 | Invariants live **on the aggregate**, not in the handler. Handlers orchestrate; aggregates enforce. |
| C7 | Aggregate-owned collections are encapsulated (`IReadOnlyCollection<>` + mutator methods + EF backing fields). No public `List<>` setters on roots. |
| C8 | Domain unit tests cover every invariant/calculation with **no EF / no container**. Handlers get integration tests against the MSSQL Testcontainer as today. |
| C9 | Flat lookup tables with no invariants (`Tag`, `Publisher`, `Genre`) stay plain — do not over-aggregate. |
| C10 | Commands model **user intentions/gestures, not table columns**. A gesture that sets several fields at once is *one* command (e.g. `MarkBookRead` = status + rating + notes), not three — atomic, one save, one `UpdatedAt` bump. A field-level command is correct only when the UI fires that field on its own (e.g. the detail page's independent inline rating / status / notes auto-saves). Never reflexively emit a CRUD command per property. *(Added after the PR1a gate — the pilot's first cut decomposed "mark read" into three field commands; see retro.)* |

## The arc (PR breakdown)

Squash-merged, one PR at a time, branched off fresh `main`. High-effort review runs **once at
arc close**, not per-PR (mid-arc reviews flag the next PR's not-yet-done work as false negatives).

| PR | Title | Scope | Risk |
|----|-------|-------|------|
| **PR0** | *(this doc)* + empty `Application` project wired into DI | This design doc. Add `BookTracker.Application` to the slnx, reference it from Web, register DI. **No behaviour change.** | Trivial |
| **PR1** | Pilot: `Book` aggregate end-to-end | Rich `Book`/`Edition`/`Copy`; `Application/Books/` (≥1 command + ≥1 query handler); rewire `BookListViewModel` + `BookDetailViewModel` to call handlers; domain unit tests. **The trial — we measure how it feels before scaling.** | Medium |
| PR2 | `Work` feature folder | Same pattern for Works + its ViewModels. | Medium |
| PR3 | `Series` feature folder | Series + the series-order/gap logic onto the aggregate (read-heavy). | Medium |
| PR4 | `Wishlist` feature folder | Smallest write surface; also its API endpoint. | Low |
| PR5 | Merge operations as commands | `BookMergeService` / `WorkMergeService` / `AuthorMergeService` → command handlers (already transactional, already aggregate-shaped). | Medium |
| PR6 | Relocate read-models | `CatalogSnapshotService` → `Application/Catalog/`; remaining list/detail VMs onto query handlers. | Low–Med |
| PR_close | Retro + docs | Update `ARCHITECTURE.md`, retro to memory, blog candidates, move TODO row Open→Shipped. | Trivial |

**Gate after PR1:** stop and evaluate. If the pattern adds friction without paying off (for
the human *or* the agent), we adjust the conventions here before rolling out PR2+.

## Open questions to resolve before/within PR1

- **Exception → UI mapping.** How do `NotFoundException` / validation failures from handlers
  surface to the user (snackbar)? Establish the pattern in PR1.
- **Handler registration.** ✅ *Resolved (PR1b):* handlers implement
  `ICommandHandler<TCommand>` / `ICommandHandler<TCommand, TResult>`; commands carry an
  `ICommand` / `ICommand<TResult>` marker; consumers inject a Scoped `IDispatcher` that
  resolves the handler by command type. **Registration is a convention scan** —
  `AddApplicationLayer` walks the assembly and registers every `ICommandHandler<>` implementer
  against its closed interface, so implementing the interface *is* the registration and a
  handler can't be forgotten. *This reverses the PR0 "no assembly scan" stance* (see non-goals):
  that call predated the evidence — at 12 handlers and growing, the explicit list's
  forgot-to-register footgun outweighed its one-file greppability, and `grep ": ICommandHandler<"`
  recovers the same list. No attribute (the interface is already the marker), no MediatR.
- **Transaction ownership.** Single `SaveChanges` per handler is the default; multi-aggregate
  writes (merges) keep explicit `BeginTransaction`. Confirm no handler spans two aggregates
  without a transaction.

---

*Companion to `ARCHITECTURE.md` (current state) — this doc describes the target state and the
path there. Once the arc closes, the durable parts fold into `ARCHITECTURE.md` and this doc
becomes historical.*
