---
name: project-backend-refactor-arc
description: "Progress + next steps for the DDD/CQRS \"pragmatic spine\" back-end refactor arc (TODO"
metadata: 
  node_type: memory
  type: project
  originSessionId: e24ce96e-656e-429c-bb6b-16afa01f73d9
---

The pragmatic-spine DDD/CQRS refactor (TODO #55, design in `docs/BACKEND-REFACTOR-DESIGN.md`, conventions C1–C11). Each PR: rich aggregate (invariants on the entity) + `BookTracker.Application` command handlers + thin hand-rolled `IDispatcher` (convention-scan registration, no MediatR); ViewModels inject `IDispatcher` and dispatch, signatures unchanged so Razor/dialogs are untouched. **Write-only** adoption — reads stay on `DbContextFactory` in the VMs, deferred to PR6.

**Done (all merged as of 2026-06-23):**
- PR1 — Book aggregate pilot (#... ; the trial). PR2 — Work (ref-count Book↔Work lifecycle, C11). PR3 — Series (#362 aggregate+handlers, #363 adopt VM). PR4 — Wishlist (#364, single PR: aggregate + 4 handlers + adopt VM).

**Remaining:**
- **PR5 — Merge operations as commands.** `BookMergeService` / `WorkMergeService` / `AuthorMergeService` → command handlers (already transactional + aggregate-shaped). Medium.
- **PR6 — Relocate read-models.** `CatalogSnapshotService` → `Application/Catalog/`; the list/detail VM **reads** (the C1 deferral from PR1–4, incl. Wishlist's snapshot API endpoint) onto query handlers. Low–Med.
- **PR_close** — retro to memory, update `ARCHITECTURE.md`, blog candidates, move TODO #55 Open→Shipped, and run the **high-effort review once at arc close** ([[feedback_review_at_arc_end]]).

**Process notes:** the doc says review-at-arc-close, but Drew has asked for a per-PR review before pushing on PR3 + PR4 — both found **zero reachable bugs** (8 finder angles → verify; method in [[feedback_review_agents_need_diff_file]]). Present findings + WAIT ([[feedback_review_findings_gate]]). Recurring fix patterns: route entity construction through aggregate factories not raw `new` (PR4: `book.AddEdition`); centralise duplicated predicates ([[feedback_overloaded_field_shared_predicate]]). Pilot lessons in [[retro_backend_ddd_pilot]].

**Next session — before starting PR5:** Drew wants a **tech-debt recon** (`docs/TECH-DEBT.md`) for items worth paying down *as part of* the remaining arc work rather than separately. Obvious arc-adjacent one: **TD-15** (shared find-or-create / `TagResolver` across Publisher/Author/Tag) — PR5's merge handlers and the deferred Add-flow resolvers both touch that surface, so a shared resolver could land naturally there. Skim the rest of TD-15-era rows for anything PR5/PR6 will pass through.
