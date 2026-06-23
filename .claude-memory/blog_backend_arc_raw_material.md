---
name: blog-backend-arc-raw-material
description: "Conversation-only narrative beats from the DDD/CQRS refactor arc (PR1-4) for the planned end-of-arc mega-blog — won't survive in git/commits."
metadata: 
  node_type: memory
  type: project
  originSessionId: e24ce96e-656e-429c-bb6b-16afa01f73d9
---

Raw material for the close-out mega-blog on the pragmatic-spine refactor ([[project-backend-refactor-arc]], [[retro-backend-ddd-pilot]], indexed in [[blog-post-backlog]]). These beats live in conversation, not commits — captured now (2026-06-23, after PR4) before compaction. Verify each against the merged code when writing.

**Thesis / the "why" (Drew's framing):** a main reason for the arc was to build an **agent-safe review/update surface** — so any local change is either *contained* or its wider blast radius is *surfaced*. Two complementary halves: **compiler-enforced seams** (project boundaries + record-typed contracts) surface *structural* blast radius at `dotnet build` (the doc's headline goal), and the **behaviour-preserving test adoption** surfaces *behavioural* blast radius the compiler can't see — which is precisely why the "no-op refactor kept finding bugs" (below) happened. Contain-or-surface is the spine the whole blog hangs on.

**Spine — the two most universal lessons:**

- **"The no-op refactor that kept finding bugs."** Behaviour-preserving adoption (lift inline VM writes into aggregates + handlers, keep method signatures so Razor is untouched) repeatedly surfaced *pre-existing latent bugs*, because lifting code forces you to name what the old code actually did. PR1: a notes-wipe data-loss (caught by adopting against the existing tests). PR3: `RemoveWorkFromSeries` left a dangling `SeriesOrderDisplay` — a stranded "4.5" on a work no longer in any series; `ClearSeries` now clears all three fields. PR4: `MarkAsBought` built a Book from a *stale in-memory row* → a duplicate book on a double-click; fixed by loading the item by id and returning null if it's already gone.

- **Solo-user severity calibration.** Per-PR adversarial review found real-but-*unreachable* concurrency findings nearly every time (`SeriesNameGuard` check-then-act TOCTOU; the Publisher/Author/Tag find-or-create race, TD-15). The discipline that emerged: fix the cheap UX ones, **document-and-accept** the unreachable ones *with the rationale written down*. "Correct" can lose to "a single user will never hit this" — but you record why, so future-you (or a second writer) can revisit when the assumption breaks.

**Vignettes:**

- **The aggregate boundary moves under you (PR2).** Drew reframed mid-arc — "is a Work more central than a Book?" — which produced the ref-count lifecycle: a Work is born attached to its first Book and self-orphans at ref-count 0; the orphan-delete is *signalled* by the aggregate (`RemoveFrom` returns "now orphaned") but *executed* by the handler. Became convention C11. The aggregate boundary you start a refactor with is not the one you finish with.

- **A naming convention's hidden Nth-case cost (PR3).** Naming the feature folder after the aggregate works because the plural dodges the type name (`Works/` vs the `Work` type) — until `Series` (singular == plural) makes the namespace collide with the type and forces a `SeriesAggregate` alias. The convention was fine for 2 of 3 instances.

- **Half-rich, half-raw graph assembly (PR4).** The promotion handler used `Work.Create` (rich factory) but hand-rolled `new Edition { Copies = [new Copy] }` — review caught it; `book.AddEdition` is the seam that owns the Edition-has-a-Copy invariant + ISBN normalisation. Lesson: use the factory *everywhere* or the next reader can't tell whether bypassing it was deliberate.

**Meta-layer (a blog in itself):** reviewing your *own agent's* work with N adversarial sub-agent angles on a handed-off diff (subagents can't reliably run git — [[feedback-review-agents-need-diff-file]]), recall-biased single-vote verify, present-findings-and-wait ([[feedback-review-findings-gate]]). Zero reachable bugs across PR1-4 — the value was as much the *documented-accept* decisions as the catches.

(PR5 merges-as-commands + PR6 read-model relocation will add beats; fold everything into the close-out blog + retro at arc end.)
