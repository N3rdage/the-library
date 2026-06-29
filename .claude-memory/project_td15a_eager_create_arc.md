---
name: project_td15a_eager_create_arc
description: "TD-15a eager-create-at-picker arc — progress, Option B design, PR1 done + review residue, PR2/3/4 remaining."
metadata: 
  node_type: memory
  type: project
  originSessionId: 5ec4e745-b284-4a27-9c36-ddd8c1565e28
---

TD-15a (the remaining strand of TD-15 in `docs/TECH-DEBT.md`): make lookup creation **eager at the picker** so the aggregate save doesn't own lookup inserts, and the (near-impossible, single-user) check-then-insert race moves to small dedicated `CreateX` commands. Planned + approved 2026-06-28; full arc, sequential PRs.

**Design = Option B (Drew's call).** Pickers eager-create on commit; the aggregate save **keeps find-or-create as a net** (so ISBN-lookup *prefill* names — which bypass the picker commit path — are still handled). **No names→ids contract change** — far less churn than the "load-only save" Option A (rejected as fragile to prefill, and the codebase keeps find-or-create for bulk/system paths anyway). Create gesture = the existing commit (Enter/comma/pick / +button); orphans accepted (Drew: "any create is a risk of orphan", shrinks as catalogue fills).

**Scope (from the 2026-06-28 picker + command inventory):**
- **Author + Contributor** → eager. (PR1, done)
- **Publisher** → eager. (PR2)
- **Series** → eager via the existing `CreateSeries` command. (PR3)
- **Genre** → OUT: no create path (preset catalogue, attach-by-id only).
- **Tag** → already eager (`AddTagToBook` create+attach on Book Detail) — the reference pattern; no work.
- **Bulk Add** (author/publisher/series) + `MarkWishlistItemBought` (author + "follow-up" tag) → STAY save-time find-or-create (grid / system paths). Residual race accepted → **TD-15 closes at arc end**.

**PRs (independent under Option B — saves untouched means they don't converge on `BookAddViewModel.SaveAsync`; branch each from fresh `main`):**
- **PR1 — Author/Contributor — DONE.** Branch `feat/td15a-author-eager-create`, commits `9546180` + `45fd2c2` (review #2 fix). New `CreateAuthor` (idempotent find-or-create→id, `BookTracker.Application/Authors/CreateAuthor.cs`); `MudAuthorPicker.razor` + `MudContributorPicker.razor` `await Dispatcher.Send(new CreateAuthor(name))` on commit (best-effort try/catch); saves unchanged. 694 tests, clean `--no-incremental` TWAE. **Awaiting Drew push + dogfood.**
  - **High-effort review residue (2026-06-28):** (1) orphan rows on chip-remove/abandon — **accepted by design** (Drew's orphan call); chip-remove-after-typo leak is the sharp case, possible future "delete-if-just-created-and-unused", not now. (2) **DONE (PR1 commit `45fd2c2`):** the `Dispatcher.Send` is now wrapped in try/catch in both pickers — logs a warning and **adds the chip anyway** (eager-create is best-effort; the save's find-or-create net guarantees the row). Test `OnCommitKey_EagerCreateThrows_StillAddsChip`. Also neutralises (3) the check-then-insert race (now non-fatal). (4) per-commit DB round-trip latency — minor for the common path, **accepted**; if it feels laggy in dogfooding, the option is fire-and-forget the dispatch.
- **PR2 — Publisher.** New `CreatePublisher` command (mirror CreateAuthor; reuse `PublisherResolver`). The two publisher `MudAutocomplete<string>` (free-text `@bind-Value`) eager-create on commit — `EditionCopyForm.razor` (~78-88) + `EditionFormDialog.razor` (~62-72). Publisher has **no explicit commit** like the author chip — need a commit trigger (autocomplete ValueChanged / OnBlur / select). Save keeps `PublisherResolver.ResolveAsync` net.
- **PR3 — Series.** Reuse existing `CreateSeries`. Accept-new-suggestion flow dispatches it eagerly and stores `AcceptedSeriesId` so the save attaches by id (already supported). Touches `BookAddViewModel` (AcceptSeriesSuggestion) + `BulkAddViewModel` per-row accept. `WorkEditDialog` is existing-series-only (optional: add create-new there).
- **PR4 — close-out.** Apply the try/catch best-effort pattern consistently; confirm BulkAdd/system residuals; move TD-15 Open→Resolved (residual accepted) in `docs/TECH-DEBT.md`; update `ARCHITECTURE.md` if warranted; arc retro.

**Conventions for this arc:** branch from fresh `main` per PR; never push/PR (Drew does — [[feedback_github_push]]); commit locally ([[feedback_commit_locally]]); the per-PR reviews here are at Drew's explicit request (normally [[feedback_review_at_arc_end]]); verify TWAE with `--no-incremental` ([[feedback_verify_warnings_clean_build]]); tests `dotnet test BookTracker.Tests --filter "Category!=E2E"`.

Key files: pickers under `BookTracker.Web/Components/Shared/`; `CreateAuthor`/`CreateSeries` commands in `BookTracker.Application/{Authors,Series}/`; resolvers `Author/Publisher/Tag/SeriesResolver`; save sites `BookAddViewModel.SaveAsync` (~480 publisher, 537/607/700 authors, 643 series) + `BulkAddViewModel` (~196/205/230); commands `CreateWorkOnBook`/`UpdateWork`/`AttachWorksToBook` take author *names* (unchanged under B). Prior context: [[retro_backend_ddd_pilot]], [[feedback_overloaded_field_shared_predicate]].
